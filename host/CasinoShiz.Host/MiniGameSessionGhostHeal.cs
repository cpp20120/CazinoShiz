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
    IFootballBetStore football) : IMiniGameSessionGhostHeal
{
    public async Task<bool> TryClearGhostIfDbEmptyAsync(long userId, long chatId, string blockingGameId, CancellationToken ct)
    {
        switch (blockingGameId)
        {
            case MiniGameIds.DiceCube:
                if (await diceCube.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.DiceCube);
                return true;
            case MiniGameIds.Darts:
                if (await darts.CountActiveByUserChatAsync(userId, chatId, ct) > 0) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Darts);
                return true;
            case MiniGameIds.Basketball:
                if (await basketball.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Basketball);
                return true;
            case MiniGameIds.Bowling:
                if (await bowling.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Bowling);
                return true;
            case MiniGameIds.Football:
                if (await football.FindAsync(userId, chatId, ct) != null) return false;
                BotMiniGameSession.ClearCompletedRound(userId, chatId, MiniGameIds.Football);
                return true;
            default:
                return false;
        }
    }
}
