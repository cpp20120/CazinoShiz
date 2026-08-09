using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public sealed class ShVoteAction : IGameAction<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult>
{
    public GameDecision<SecretHitlerExecutionState, ShVoteResult> Decide(
        GameActionInput<SecretHitlerExecutionState, ShVoteCommand> input)
    {
        if (input.State.Game is null) return Reject(input.State, ShError.NotInGame);
        var state = SecretHitlerExecutionRules.Clone(input.State);
        var actor = state.Players.FirstOrDefault(p => p.UserId == input.Command.ActorUserId);
        if (actor is null) return Reject(input.State, ShError.NotInGame);
        var validation = ShTransitions.ValidateVote(state.Game!, actor);
        if (validation != ShValidation.Ok) return Reject(input.State, MapValidation(validation));
        var after = ShTransitions.ApplyVote(state.Game!, actor, input.Command.Vote, state.Players,
            SecretHitlerExecutionRules.ReshuffleEntropyNames.Select(input.Entropy.GetDouble).ToArray());
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

    private static GameDecision<SecretHitlerExecutionState, ShVoteResult> Reject(
        SecretHitlerExecutionState state, ShError error) =>
        new(DecisionStatus.Rejected, state, VoteFail(error), [], [], [], [], [], error.ToString());
}
