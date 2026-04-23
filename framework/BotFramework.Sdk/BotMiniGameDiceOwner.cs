using System.Collections.Concurrent;
using Telegram.Bot.Types;

namespace BotFramework.Sdk;

/// <summary>
/// When the bot sends dice with <c>replyParameters</c>, Telegram sometimes omits
/// <see cref="Message.ReplyToMessage"/> on the final <see cref="Update.EditedMessage"/>
/// (e.g. groups + privacy). We bind <c>(chatId, diceMessageId) → player</c> at send time
/// and fall back here if <see cref="MiniGameDicePlayer.TryResolvePlayer"/> fails.
/// </summary>
public static class BotMiniGameDiceOwner
{
    private static readonly ConcurrentDictionary<(long ChatId, int MessageId), Entry> Map = new();

    private sealed record Entry(long UserId, string DisplayName, long UntilTicks);

    /// <summary>Remember who owns this bot dice until roll is processed or TTL.</summary>
    public static void Bind(long chatId, int messageId, long userId, string displayName) =>
        Map[(chatId, messageId)] = new Entry(userId, displayName, Environment.TickCount64 + 120_000);

    public static void Unbind(long chatId, int messageId) =>
        Map.TryRemove((chatId, messageId), out _);

    /// <summary>Same as <see cref="MiniGameDicePlayer.TryResolvePlayer"/> plus bound bot dice.</summary>
    public static bool TryResolveDicePlayer(Message diceMessage, out long userId, out string displayName)
    {
        if (MiniGameDicePlayer.TryResolvePlayer(diceMessage, out userId, out displayName))
            return true;

        if (diceMessage.From is not { IsBot: true })
            return false;

        return TryGetBinding(diceMessage.Chat.Id, diceMessage.MessageId, out userId, out displayName);
    }

    private static bool TryGetBinding(long chatId, int messageId, out long userId, out string displayName)
    {
        userId = 0;
        displayName = "";
        if (!Map.TryGetValue((chatId, messageId), out var row))
            return false;
        if (Environment.TickCount64 > row.UntilTicks)
        {
            Map.TryRemove((chatId, messageId), out _);
            return false;
        }

        userId = row.UserId;
        displayName = row.DisplayName;
        return true;
    }
}
