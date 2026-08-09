using BotFramework.Sdk.Execution;
using Games.SecretHitler.Application.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerCreateAction
    : IGameAction<ShCreateCommand, SecretHitlerWagerState, ShCreateResult>
{
    public GameDecision<SecretHitlerWagerState, ShCreateResult> Decide(
        GameActionInput<SecretHitlerWagerState, ShCreateCommand> input)
    {
        var c = input.Command;
        if (c.WagerBetId is null) return Reject(input.State, ShError.NotInGame);
        if (input.State.Game is not null) return Reject(input.State, ShError.GameInProgress);
        if (input.State.Players.Any(player => player.UserId == c.ActorUserId))
            return Reject(input.State, ShError.AlreadyInGame);
        if (c.BuyIn <= 0) return Reject(input.State, ShError.NotEnoughCoins);
        var now = input.UtcNow.ToUnixTimeMilliseconds();
        var code = SecretHitlerExecutionRules.InviteCode(input.Entropy.GetDouble(SecretHitlerExecutionRules.InviteEntropy));
        var game = new SecretHitlerGame
        {
            InviteCode = code, HostUserId = c.ActorUserId, ChatId = c.PublicChatId,
            Status = ShStatus.Lobby, Phase = ShPhase.None, BuyIn = c.BuyIn, Pot = c.BuyIn,
            CreatedAt = now, LastActionAt = now,
        };
        var player = new SecretHitlerPlayer
        {
            InviteCode = code, Position = 0, UserId = c.ActorUserId,
            DisplayName = c.DisplayName, ChatId = c.ActorChatId, IsAlive = true,
            JoinedAt = now, WagerBetId = c.WagerBetId,
        };
        return new(DecisionStatus.Accepted, new(game, [player]), new(ShError.None, code, c.BuyIn),
            [], [], [], [new SecretHitlerGameCreated(code, c.ActorUserId, c.BuyIn, now)], []);
    }

    private static GameDecision<SecretHitlerWagerState, ShCreateResult> Reject(
        SecretHitlerWagerState state, ShError error) =>
        new(DecisionStatus.Rejected, state, CreateFail(error), [], [], [], [], [], error.ToString());
}
