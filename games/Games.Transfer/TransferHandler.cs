using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Transfer;

[Command("/transfer")]
public sealed class TransferHandler(
    ITransferService transfers,
    ILocalizer localizer,
    ITelegramBotClient bot,
    IOptions<TransferOptions> options) : IUpdateHandler
{
    private readonly TransferOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (!msg.Text.StartsWith("/transfer", StringComparison.OrdinalIgnoreCase)) return;

        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var chatId = msg.Chat.Id;

        if (msg.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
        {
            await ctx.Bot.SendMessage(chatId, Loc("err.groups_only"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var fromId = msg.From?.Id ?? 0;
        if (fromId == 0) return;

        if (!TryParseAmount(msg, out var net) || net <= 0)
        {
            await ctx.Bot.SendMessage(chatId, Loc("usage"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var target = await TryResolveTargetAsync(msg, ctx.Ct);
        if (target is null)
        {
            await ctx.Bot.SendMessage(chatId, Loc("err.no_target"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var (toId, recipientLabel) = target.Value;
        if (toId == fromId)
        {
            await ctx.Bot.SendMessage(chatId, Loc("err.self"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var senderName = msg.From?.Username is { Length: > 0 } su
            ? $"@{su}"
            : msg.From?.FirstName ?? $"User ID: {fromId}";

        var result = await transfers.TryTransferAsync(
            fromId, toId, chatId, senderName, recipientLabel, net, ctx.Ct);

        switch (result.Error)
        {
            case TransferError.NetBelowMinimum:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("err.min_net"), _opts.MinNetCoins),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case TransferError.NetAboveMaximum:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("err.max_net"), _opts.MaxNetCoins),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case TransferError.SameUser:
                await ctx.Bot.SendMessage(chatId, Loc("err.self"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case TransferError.InsufficientFunds:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("err.balance"), result.TotalDebited, result.SenderBalance),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case TransferError.None:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("ok"),
                        net,
                        result.FeeCoins,
                        result.TotalDebited,
                        result.SenderBalance,
                        result.RecipientBalance,
                        recipientLabel),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            default:
                await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
        }
    }

    private static bool TryParseAmount(Message msg, out int net)
    {
        net = 0;
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        return int.TryParse(parts[^1], out net) && net > 0;
    }

    private async Task<(long userId, string display)?> TryResolveTargetAsync(Message msg, CancellationToken ct)
    {
        if (msg.ReplyToMessage?.From is { IsBot: false } ru)
            return (ru.Id, FormatUserLabel(ru));

        foreach (var e in msg.Entities ?? [])
        {
            if (e.Type == MessageEntityType.TextMention && e.User is { IsBot: false } u)
                return (u.Id, FormatUserLabel(u));
        }

        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && long.TryParse(parts[1], out var uid) && uid > 0)
            return (uid, $"User ID: {uid}");

        if (parts.Length >= 3)
        {
            var handle = parts[1].TrimStart('@');
            if (handle.Length == 0) return null;
            try
            {
                var chat = await bot.GetChat(new Telegram.Bot.Types.ChatId("@" + handle), ct);
                if (chat.Type != ChatType.Private) return null;
                var label = chat.Username is { Length: > 0 } un ? $"@{un}" : chat.FirstName ?? $"User ID: {chat.Id}";
                return (chat.Id, label);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string FormatUserLabel(User u) =>
        u.Username is { Length: > 0 } name ? $"@{name}" : u.FirstName ?? $"User ID: {u.Id}";

    private string Loc(string key) => localizer.Get("transfer", key);
}
