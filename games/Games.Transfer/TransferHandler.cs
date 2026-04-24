using System.Text.RegularExpressions;
using BotFramework.Host;
using BotFramework.Host.Composition;
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
    IOptions<TransferOptions> options,
    IOptions<BotFrameworkOptions> frameworkOpts) : IUpdateHandler
{
    private readonly TransferOptions _opts = options.Value;
    private readonly BotFrameworkOptions _framework = frameworkOpts.Value;

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
        var parts = SplitArgs(msg.Text!);
        if (parts.Length < 2) return false;
        return int.TryParse(parts[^1], out net) && net > 0;
    }

    /// <summary>Telegram and clients may use NBSP / other Unicode space; <see cref="string.Split(char)"/> misses those.</summary>
    private static string[] SplitArgs(string text) =>
        Regex.Split(text.Trim(), @"\s+", RegexOptions.None)
            .Where(static s => s.Length > 0)
            .ToArray();

    private async Task<(long userId, string display)?> TryResolveTargetAsync(Message msg, CancellationToken ct)
    {
        foreach (var e in msg.Entities ?? [])
        {
            if (e.Type == MessageEntityType.TextMention && e.User is { IsBot: false } u)
                return (u.Id, FormatUserLabel(u));
        }

        // Plain "@username" typed or chosen from autocomplete is MessageEntityType.Mention, not TextMention.
        foreach (var e in msg.Entities ?? [])
        {
            if (e.Type != MessageEntityType.Mention) continue;
            if (MentionIsInsideBotCommand(msg, e)) continue;
            if (msg.Text is null || e.Offset < 0 || e.Offset + e.Length > msg.Text.Length) continue;
            var handle = msg.Text.Substring(e.Offset, e.Length).TrimStart('@');
            if (handle.Length == 0) continue;
            var resolved = await TryResolvePrivateUserByUsernameAsync(handle, ct);
            if (resolved != null) return resolved;
        }

        var parts = SplitArgs(msg.Text!);
        if (parts.Length >= 3 && int.TryParse(parts[^1], out _))
        {
            var midCount = parts.Length - 2;
            var recipientToken = midCount == 1
                ? parts[1]
                : string.Join(' ', parts.Skip(1).Take(midCount));
            if (recipientToken.Length > 0)
            {
                if (long.TryParse(recipientToken, out var uid) && uid > 0)
                    return (uid, $"User ID: {uid}");

                var h = recipientToken.TrimStart('@');
                if (h.Length > 0)
                {
                    var byName = await TryResolvePrivateUserByUsernameAsync(h, ct);
                    if (byName != null) return byName;
                }
            }
        }

        // Only if the message does not name a recipient: use reply (avoids "/transfer 10" replying to X overriding nothing).
        if (msg.ReplyToMessage?.From is { IsBot: false } ru)
            return (ru.Id, FormatUserLabel(ru));

        return null;
    }

    private async Task<(long userId, string display)?> TryResolvePrivateUserByUsernameAsync(string handle, CancellationToken ct)
    {
        handle = handle.Trim();
        if (handle.Length == 0) return null;

        var botName = _framework.Username.Trim().TrimStart('@');
        if (botName.Length > 0 && string.Equals(handle, botName, StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var chat = await bot.GetChat(new ChatId("@" + handle), ct);
            if (chat.Type != ChatType.Private) return null;
            if (chat.Username is { Length: > 0 } un &&
                botName.Length > 0 &&
                string.Equals(un, botName, StringComparison.OrdinalIgnoreCase))
                return null;

            var label = chat.Username is { Length: > 0 } u2 ? $"@{u2}" : chat.FirstName ?? $"User ID: {chat.Id}";
            return (chat.Id, label);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Skip @handles that are only the /command@BotName suffix (separate Mention entity in some clients).</summary>
    private static bool MentionIsInsideBotCommand(Message msg, MessageEntity mention)
    {
        if (msg.Text is null) return false;
        var textLen = msg.Text.Length;
        foreach (var e in msg.Entities ?? [])
        {
            if (e.Type != MessageEntityType.BotCommand) continue;
            var cmdLo = e.Offset;
            var cmdHi = e.Offset + e.Length;
            // Some clients mark the entire message as BotCommand; then every Mention looks "inside" and we skip all.
            if (cmdHi - cmdLo >= textLen)
                continue;
            var mLo = mention.Offset;
            var mHi = mention.Offset + mention.Length;
            if (mLo >= cmdLo && mHi <= cmdHi)
                return true;
        }

        return false;
    }

    private static string FormatUserLabel(User u) =>
        u.Username is { Length: > 0 } name ? $"@{name}" : u.FirstName ?? $"User ID: {u.Id}";

    private string Loc(string key) => localizer.Get("transfer", key);
}
