using BotFramework.Sdk.Execution;

namespace Games.Blackjack.Application.Execution;

internal static class BlackjackWagerConstants
{
    public const string GameId = "blackjack";
    public const string StateGameId = "blackjack-wager";

    public static BlackjackWagerState InitialState(
        string betId,
        string playerId,
        long chatId,
        string displayName) =>
        new(
            0,
            betId,
            playerId,
            chatId,
            displayName,
            TurnGameStatus.Completed,
            playerId,
            null,
            [],
            [],
            string.Empty,
            null,
            null,
            null);
}
