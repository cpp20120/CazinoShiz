using BotFramework.Sdk.Execution;
using Games.Poker.Domain.Events;

namespace Games.Poker.Application.Execution;

public sealed class PokerLeaveAction
    : IGameAction<PokerLeaveCommand, PokerExecutionState, LeaveResult>
{
    public GameDecision<PokerExecutionState, LeaveResult> Decide(
        GameActionInput<PokerExecutionState, PokerLeaveCommand> input)
    {
        if (input.State.Table is null)
            return Reject(input.State, PokerError.NoTable);
        var state = PokerExecutionRules.Clone(input.State);
        var seat = state.Seats.FirstOrDefault(item => item.UserId == input.Command.ActorUserId);
        if (seat is null) return Reject(input.State, PokerError.NoTable);
        var effects = new List<IGameEffect>();
        var events = new List<IDomainEvent>();
        if (seat.WagerBetId is { } betId)
        {
            events.Add(new PokerWagerOutcomeDeclared(
                betId,
                seat.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "refund",
                System.Text.Json.JsonSerializer.Serialize(new { payout = seat.Stack }),
                input.UtcNow.ToUnixTimeMilliseconds()));
        }
        else if (seat.Stack > 0)
            effects.Add(WalletEconomyEffect.Credit(seat.UserId, seat.ChatId, seat.Stack, "poker.leave"));

        if (state.Table!.Status == PokerTableStatus.HandActive && seat.Status == PokerSeatStatus.Seated)
        {
            seat.Status = PokerSeatStatus.Folded;
            seat.Stack = 0;
            var resolution = PokerExecutionRules.Resolve(state, input.UtcNow);
            effects.AddRange(resolution.Effects);
            events.AddRange(resolution.Events);
        }
        state.Seats.RemoveAll(item => item.UserId == input.Command.ActorUserId);
        var closed = state.Seats.Count == 0;
        if (closed) state.Table.Status = PokerTableStatus.Closed;
        var snapshot = closed ? null : PokerExecutionRules.Snapshot(state);
        return new(DecisionStatus.Accepted, state, new(PokerError.None, snapshot, closed),
            [], [], [], events, [], CustomEffects: effects);
    }

    private static GameDecision<PokerExecutionState, LeaveResult> Reject(
        PokerExecutionState state, PokerError error) =>
        new(DecisionStatus.Rejected, state, new(error, null, false), [], [], [], [], [], error.ToString());
}
