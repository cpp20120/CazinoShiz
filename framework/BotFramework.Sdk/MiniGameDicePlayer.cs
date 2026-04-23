using Telegram.Bot.Types;

namespace BotFramework.Sdk;

/// <summary>
/// Resolves the human player for Telegram mini-game dice: user-sent dice use <see cref="Message.From"/>;
/// bot-sent dice (after <c>/game bet</c>) must reply to the user's command so we read <see cref="Message.ReplyToMessage"/>.
/// </summary>
public static class MiniGameDicePlayer
{
    public static bool TryResolvePlayer(Message diceMessage, out long userId, out string displayName)
    {
        userId = 0;
        displayName = "";
        if (diceMessage.From is { IsBot: false } u)
        {
            userId = u.Id;
            displayName = Format(u);
            return true;
        }

        if (diceMessage is { From.IsBot: true, ReplyToMessage.From: { IsBot: false } ru })
        {
            userId = ru.Id;
            displayName = Format(ru);
            return true;
        }

        return false;
    }

    private static string Format(User u) =>
        u.Username ?? u.FirstName ?? $"User ID: {u.Id}";
}
