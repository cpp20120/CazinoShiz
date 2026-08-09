using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public sealed class ShLeaveAction : IGameAction<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult>
{
    public GameDecision<SecretHitlerExecutionState, ShLeaveResult> Decide(
        GameActionInput<SecretHitlerExecutionState, ShLeaveCommand> input)
    {
        if (input.State.Game is null) return Reject(input.State, ShError.NotInGame);
        var state = SecretHitlerExecutionRules.Clone(input.State);
        var actor = state.Players.FirstOrDefault(p => p.UserId == input.Command.ActorUserId);
        if (actor is null) return Reject(input.State, ShError.NotInGame);
        if (state.Game!.Status == ShStatus.Active) return Reject(input.State, ShError.GameInProgress);
        state.Game.Pot = Math.Max(0, state.Game.Pot - state.Game.BuyIn);
        state.Game.LastActionAt = input.UtcNow.ToUnixTimeMilliseconds();
        state.Players.Remove(actor);
        var closed = state.Players.Count == 0;
        if (closed) state.Game.Status = ShStatus.Closed;
        IDomainEvent[] events = actor.WagerBetId is null
            ? []
            : [new SecretHitlerWagerOutcomeDeclared(actor.WagerBetId, actor.UserId.ToString(), "refund",
                System.Text.Json.JsonSerializer.Serialize(new { payout = state.Game.BuyIn }),
                input.UtcNow.ToUnixTimeMilliseconds())];
        return new(DecisionStatus.Accepted, state,
            new(ShError.None, closed ? null : SecretHitlerExecutionRules.Snapshot(state), closed),
            [], [], [], events, [], CustomEffects: actor.WagerBetId is null
                ? [WalletEconomyEffect.Credit(actor.UserId, actor.ChatId, state.Game.BuyIn, "sh.leave")]
                : []);
    }

    private static GameDecision<SecretHitlerExecutionState, ShLeaveResult> Reject(
        SecretHitlerExecutionState state, ShError error) =>
        new(DecisionStatus.Rejected, state, LeaveFail(error), [], [], [], [], [], error.ToString());
}
