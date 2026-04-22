// ─────────────────────────────────────────────────────────────────────────────
// HorseHandler — dispatches /horse (bet/result/info/help) and /horserun.
// /horserun is silently rejected for non-admins; the admin list lives in
// HorseOptions.Admins (bound from configuration).
//
// Winner announcement happens 20s after the GIF drops. The update's ct is
// tied to the per-update scope (polling iteration or webhook request) — it
// can be cancelled before the delay elapses. We use the host lifetime
// ApplicationStopping token so the announce survives the scope but still
// cancels cleanly on shutdown.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Horse;

[Command("/horse")]
[Command("/horserun")]
public sealed partial class HorseHandler(
    IHorseService service,
    ILocalizer localizer,
    IOptions<HorseOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<HorseHandler> logger) : IUpdateHandler
{
    private readonly HorseOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        if (msg.Text.StartsWith("/horserun"))
        {
            await HandleRunAsync(ctx, msg, userId);
            return;
        }

        var parts = StripFirst(msg.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 0 ? parts[0] : "";

        var reply = new ReplyParameters { MessageId = msg.MessageId };
        switch (action)
        {
            case "bet": await HandleBetAsync(ctx, msg, userId, parts); break;
            case "result": await HandleResultAsync(ctx, msg); break;
            case "info": await HandleInfoAsync(ctx, msg); break;
            case "help":
                await ctx.Bot.SendMessage(msg.Chat.Id, string.Format(Loc("help"), _opts.HorseCount),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
            default:
                await ctx.Bot.SendMessage(msg.Chat.Id, string.Format(Loc("unknown_action"), action),
                    replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task HandleBetAsync(UpdateContext ctx, Message msg, long userId, string[] parts)
    {
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (parts.Length < 2 || !int.TryParse(parts[1], out int horseId))
        {
            await ctx.Bot.SendMessage(chatId, string.Format(Loc("bet.no_horse"), _opts.HorseCount),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }
        if (parts.Length < 3 || !int.TryParse(parts[2], out int amount))
        {
            await ctx.Bot.SendMessage(chatId, Loc("bet.no_amount"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var r = await service.PlaceBetAsync(userId, displayName, chatId, horseId, amount, ctx.Ct);

        var text = r.Error switch
        {
            HorseError.None => string.Format(Loc("bet.accepted"), r.Amount, r.HorseId),
            HorseError.InvalidHorseId => string.Format(Loc("bet.invalid_horse"), _opts.HorseCount),
            HorseError.InvalidAmount => string.Format(Loc("bet.invalid_amount"), r.RemainingCoins),
            _ => Loc("bet.failed"),
        };
        await ctx.Bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private async Task HandleRunAsync(UpdateContext ctx, Message msg, long userId)
    {
        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        var outcome = await service.RunRaceAsync(userId, ctx.Ct);
        if (outcome.Error == HorseError.NotAdmin) return;
        if (outcome.Error == HorseError.NotEnoughBets)
        {
            await ctx.Bot.SendMessage(chatId, string.Format(Loc("run.not_enough_bets"), _opts.MinBetsToRun),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        await ctx.Bot.SendMessage(chatId, Loc("run.started"),
            replyParameters: reply, cancellationToken: ctx.Ct);

        Message gifMessage;
        await using (var gifStream = new MemoryStream(outcome.GifBytes))
            gifMessage = await ctx.Bot.SendAnimation(chatId, InputFile.FromStream(gifStream, "horses.gif"),
                cancellationToken: ctx.Ct);

        var raceDate = HorseTimeHelper.GetRaceDate();
        var fileId = gifMessage.Animation?.FileId;
        if (fileId != null)
            await service.SaveFileIdAsync(raceDate, fileId, ctx.Ct);

        var announceCt = lifetime.ApplicationStopping;
        var bot = ctx.Bot;
        var transactions = outcome.Transactions;
        var delayMs = _opts.AnnounceDelayMs;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, announceCt);
                var text = transactions.Count > 0
                    ? string.Join("\n", new[] { Loc("run.winners_header") + "\n" }
                        .Concat(transactions.Select((tx, i) =>
                            string.Format(Loc("run.winner_line"), i + 1, tx.UserId, tx.Amount))))
                    : Loc("run.no_winners");
                await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: announceCt);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogHorseRunAnnounceFailed(ex); }
        }, announceCt);
    }

    private async Task HandleResultAsync(UpdateContext ctx, Message msg)
    {
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var r = await service.GetTodayResultAsync(ctx.Ct);

        if (r.Winner.HasValue && r.FileId != null)
        {
            await ctx.Bot.SendAnimation(msg.Chat.Id, InputFile.FromFileId(r.FileId),
                caption: string.Format(Loc("result.winner"), r.Winner.Value + 1),
                replyParameters: reply, cancellationToken: ctx.Ct);
        }
        else
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("result.none"),
                replyParameters: reply, cancellationToken: ctx.Ct);
        }
    }

    private async Task HandleInfoAsync(UpdateContext ctx, Message msg)
    {
        var info = await service.GetTodayInfoAsync(ctx.Ct);
        var parts = new List<string> { string.Format(Loc("info.stakes_count"), info.BetsCount) };
        if (info.BetsCount > 0)
        {
            var koefs = string.Join("\n",
                info.Koefs.OrderBy(kv => kv.Key).Select(kv =>
                    string.Format(Loc("info.koef_line"), kv.Key + 1, kv.Value.ToString("F3"))));
            parts.Add(Loc("info.koefs_header") + "\n" + koefs);
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n\n", parts), parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ctx.Ct);
    }

    private static string StripFirst(string str)
    {
        var parts = str.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].Trim() : "";
    }

    private string Loc(string key) => localizer.Get("horse", key);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Debug, Message = "horse.run.announce failed")]
    partial void LogHorseRunAnnounceFailed(Exception exception);
}
