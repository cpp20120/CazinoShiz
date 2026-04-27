using BotFramework.Sdk;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.DiceCube;
using Games.Football;

namespace CasinoShiz.Host;

public sealed class MiniGameSessionGhostHeal(
    IDiceCubeBetStore diceCube,
    IDartsRoundStore darts,
    IBasketballBetStore basketball,
    IBowlingBetStore bowling,
    IFootballBetStore football,
    IMiniGameSessionStore sessions) : IMiniGameSessionGhostHeal
{
    public async Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct)
    {
        switch (blockingGameId)
        {
            case MiniGameIds.DiceCube:
                if (await diceCube.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
                await sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.DiceCube, ct);
                return true;
            case MiniGameIds.Darts:
                if (await darts.CountActiveByUserChatAsync(userId, chatId, ct) > 0) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
                await sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Darts, ct);
                return true;
            case MiniGameIds.Basketball:
                if (await basketball.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
                await sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Basketball, ct);
                return true;
            case MiniGameIds.Bowling:
                if (await bowling.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
                await sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Bowling, ct);
                return true;
            case MiniGameIds.Football:
                if (await football.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
                await sessions.ClearCompletedRoundAsync(userId, chatId, MiniGameIds.Football, ct);
                return true;
            default:
                return false;
        }
    }
}
