using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public sealed class ShEnactAction : IGameAction<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult>
{
    public GameDecision<SecretHitlerExecutionState, ShEnactResult> Decide(
        GameActionInput<SecretHitlerExecutionState, ShEnactCommand> input)
    {
        if (input.State.Game is null) return Reject(input.State, ShError.NotInGame);
        var state = SecretHitlerExecutionRules.Clone(input.State);
        var actor = state.Players.FirstOrDefault(p => p.UserId == input.Command.ActorUserId);
        if (actor is null) return Reject(input.State, ShError.NotInGame);
        var validation = ShTransitions.ValidateChancellorEnact(state.Game!, actor, input.Command.EnactIndex);
        if (validation != ShValidation.Ok) return Reject(input.State, MapValidation(validation));
        var after = ShTransitions.ApplyChancellorEnact(state.Game!, input.Command.EnactIndex, state.Players);
        state.Game!.LastActionAt = input.UtcNow.ToUnixTimeMilliseconds();
        var payouts = new List<SecretHitlerPayout>();
        var wagerOutcomes = new List<IDomainEvent>();
        var effects = state.Game.Status == ShStatus.Completed
            ? SecretHitlerExecutionRules.Settle(state, payouts, wagerOutcomes) : [];
        IDomainEvent[] events = state.Game.Status == ShStatus.Completed
            ? [new SecretHitlerGameEnded(state.Game.InviteCode, state.Game.Winner,
                state.Game.WinReason, payouts, state.Game.LastActionAt), ..wagerOutcomes] : [];
        return new(DecisionStatus.Accepted, state,
            new(ShError.None, SecretHitlerExecutionRules.Snapshot(state), after),
            [], [], [], events, [], CustomEffects: effects);
    }

    private static GameDecision<SecretHitlerExecutionState, ShEnactResult> Reject(
        SecretHitlerExecutionState state, ShError error) =>
        new(DecisionStatus.Rejected, state, EnactFail(error), [], [], [], [], [], error.ToString());
}
