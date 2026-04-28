using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Redeem;

[Command("/redeem")]
[Command("/codegen")]
[CallbackPrefix("rd:")]
public sealed partial class RedeemHandler(
    IRedeemService service,
    ILocalizer localizer,
    IOptions<RedeemOptions> options,
    IOptions<BotFrameworkOptions> botOptions,
    IHostApplicationLifetime lifetime,
    ILogger<RedeemHandler> logger) : IUpdateHandler
{
    private readonly RedeemOptions _opts = options.Value;
    private readonly BotFrameworkOptions _botOpts = botOptions.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery is { } cbq)
        {
            await HandleCallbackAsync(ctx, cbq);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Text.StartsWith("/codegen"))
            await HandleCodeGenAsync(ctx, msg);
        else if (msg.Text.StartsWith("/redeem"))
            await HandleRedeemAsync(ctx, msg);
    }

    private async Task HandleCodeGenAsync(UpdateContext ctx, Message msg)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0 || !IsCodegenAdmin(userId)) return;

        var count = ParseCodegenCount(msg.Text);
        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var code = await service.IssueAdminCodeAsync(userId, ctx.Ct);
            lines.Add($"/redeem {code}");
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join('\n', lines), cancellationToken: ctx.Ct);
    }

    private async Task HandleRedeemAsync(UpdateContext ctx, Message msg)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (msg.Chat.Type != ChatType.Private)
        {
            await ctx.Bot.SendMessage(chatId, Loc("err.only_private"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var codeText = parts.Length > 1 ? parts[1] : "";
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var result = await service.BeginRedeemAsync(userId, chatId, displayName, codeText, ctx.Ct);

        switch (result.Error)
        {
            case RedeemError.InvalidCode:
                await ctx.Bot.SendMessage(chatId, Loc("err.invalid_code"), cancellationToken: ctx.Ct);
                return;
            case RedeemError.AlreadyRedeemed:
                await ctx.Bot.SendMessage(chatId, Loc("err.already_redeemed"), cancellationToken: ctx.Ct);
                return;
            case RedeemError.SelfRedeem:
                await ctx.Bot.SendMessage(chatId, Loc("err.self_redeem"), cancellationToken: ctx.Ct);
                return;
            case RedeemError.NoUser:
                await ctx.Bot.SendMessage(chatId, Loc("err.no_user"), cancellationToken: ctx.Ct);
                return;
        }

        var captcha = result.Captcha!;
        var markup = BuildCaptchaMarkup(result.CodeGuid, captcha);

        var captchaMsg = await ctx.Bot.SendMessage(chatId,
            string.Format(Loc("captcha.prompt"), captcha.Pattern),
            parseMode: ParseMode.Html,
            replyMarkup: markup,
            cancellationToken: ctx.Ct);

        _ = ScheduleTimeoutAsync(ctx.Bot, chatId, captchaMsg.MessageId, lifetime.ApplicationStopping);
    }

    private async Task HandleCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        try { await ctx.Bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ctx.Ct); } catch { }

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? 0;
        if (chatId == 0 || cbq.Data == null) return;

        var parts = cbq.Data.Split(':');
        if (parts.Length != 3 || parts[0] != "rd") return;
        if (!Guid.TryParse(parts[1], out var codeGuid)) return;
        if (!int.TryParse(parts[2], out var chosenId)) return;

        var expected = CaptchaService.CreateCaptcha(codeGuid.ToString(), _opts.CaptchaItems);
        var passed = chosenId == expected.TargetId;

        service.ReportCaptcha(userId, codeGuid.ToString(), expected.Pattern, passed);

        try
        {
            if (cbq.Message != null)
                await ctx.Bot.DeleteMessage(chatId, cbq.Message.MessageId, cancellationToken: ctx.Ct);
        }
        catch { }

        if (!passed)
        {
            await ctx.Bot.SendMessage(chatId, Loc("captcha.wrong"), cancellationToken: ctx.Ct);
            return;
        }

        var result = await service.CompleteRedeemAsync(userId, chatId, codeGuid, ctx.Ct);
        if (result.Error == RedeemError.AlreadyRedeemed)
        {
            await ctx.Bot.SendMessage(chatId, Loc("err.lost_race"), cancellationToken: ctx.Ct);
            return;
        }

        await ctx.Bot.SendMessage(chatId, Loc("redeem.success"), cancellationToken: ctx.Ct);
    }

    private int ParseCodegenCount(string? text)
    {
        var parts = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length <= 1 || !int.TryParse(parts[1], out var requested))
            return 1;

        return Math.Clamp(requested, 1, Math.Max(1, _opts.MaxCodegenCount));
    }

    private bool IsCodegenAdmin(long userId) =>
        _opts.Admins.Contains(userId) || _botOpts.Admins.Contains(userId);

    private async Task ScheduleTimeoutAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_opts.CaptchaTimeoutMs, ct);
            try { await bot.DeleteMessage(chatId, messageId); } catch { }
            try { await bot.SendMessage(chatId, Loc("captcha.timeout")); } catch { }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogTimeoutFailed(ex); }
    }

    private static InlineKeyboardMarkup BuildCaptchaMarkup(Guid codeGuid, CaptchaResult captcha)
    {
        var rows = (int)Math.Ceiling(captcha.Items.Length / 5.0);
        var splitAfter = Math.Max(1, (int)Math.Ceiling(captcha.Items.Length / (double)rows));

        var keyboardRows = new List<InlineKeyboardButton[]>();
        for (var i = 0; i < rows; i++)
        {
            keyboardRows.Add(captcha.Items
                .Skip(i * splitAfter).Take(splitAfter)
                .Select(item => InlineKeyboardButton.WithCallbackData(item.Text, $"rd:{codeGuid}:{item.Data}"))
                .ToArray());
        }
        return new InlineKeyboardMarkup(keyboardRows);
    }

    private string Loc(string key) => localizer.Get("redeem", key);

    [LoggerMessage(LogLevel.Warning, "redeem.captcha_timeout_failed")]
    partial void LogTimeoutFailed(Exception ex);
}
