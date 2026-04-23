using BotFramework.Sdk;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.DiceCube;
using Games.Football;

namespace CasinoShiz.Tests;

/// <summary>In-memory ghost-session heal (same rules as host) for unit tests.</summary>
public sealed class LocalMiniGameSessionGhostHeal(
    IDiceCubeBetStore? diceCube = null,
    IDartsRoundStore? darts = null,
    IBasketballBetStore? basketball = null,
    IBowlingBetStore? bowling = null,
    IFootballBetStore? football = null) : IMiniGameSessionGhostHeal
{
    public async Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct)
    {
        switch (blockingGameId)
        {
            case MiniGameIds.DiceCube:
                if (diceCube is null) return false;
                if (await diceCube.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
                return true;
            case MiniGameIds.Darts:
                if (darts is null) return false;
                if (await darts.CountActiveByUserChatAsync(userId, chatId, ct) > 0) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
                return true;
            case MiniGameIds.Basketball:
                if (basketball is null) return false;
                if (await basketball.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
                return true;
            case MiniGameIds.Bowling:
                if (bowling is null) return false;
                if (await bowling.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
                return true;
            case MiniGameIds.Football:
                if (football is null) return false;
                if (await football.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
                return true;
            default:
                return false;
        }
    }
}
