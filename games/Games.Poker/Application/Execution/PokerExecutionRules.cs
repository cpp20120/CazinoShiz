using System.Text.Json;
using BotFramework.Sdk.Execution;
using Games.Poker.Domain.Events;

namespace Games.Poker.Application.Execution;

public static class PokerExecutionRules
{
    public static readonly IReadOnlyList<string> ShuffleEntropyNames =
        Enumerable.Range(0, 51).Select(index => $"shuffle-{index}").ToArray();
    public const string InviteEntropy = "invite-code";

    public static PokerExecutionState Clone(PokerExecutionState state) =>
        new(state.Table is null ? null : Clone(state.Table), state.Seats.Select(Clone).ToList(), state.ActorBalance);

    public static PokerTable Clone(PokerTable table) => new()
    {
        InviteCode = table.InviteCode, ChatId = table.ChatId, HostUserId = table.HostUserId,
        Status = table.Status, Phase = table.Phase, SmallBlind = table.SmallBlind,
        BigBlind = table.BigBlind, Pot = table.Pot, CommunityCards = table.CommunityCards,
        DeckState = table.DeckState, ButtonSeat = table.ButtonSeat, CurrentSeat = table.CurrentSeat,
        CurrentBet = table.CurrentBet, MinRaise = table.MinRaise,
        StateMessageId = table.StateMessageId, LastActionAt = table.LastActionAt,
        CreatedAt = table.CreatedAt,
    };

    public static PokerSeat Clone(PokerSeat seat) => new()
    {
        InviteCode = seat.InviteCode, Position = seat.Position, UserId = seat.UserId,
        DisplayName = seat.DisplayName, Stack = seat.Stack, HoleCards = seat.HoleCards,
        Status = seat.Status, CurrentBet = seat.CurrentBet, TotalCommitted = seat.TotalCommitted,
        HasActedThisRound = seat.HasActedThisRound, ChatId = seat.ChatId,
        StateMessageId = seat.StateMessageId, JoinedAt = seat.JoinedAt, WagerBetId = seat.WagerBetId,
    };

    public static string InviteCode(double entropy)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var value = (int)(entropy * 33_554_432d);
        Span<char> chars = stackalloc char[5];
        for (var index = chars.Length - 1; index >= 0; index--)
        {
            chars[index] = alphabet[value & 31];
            value >>= 5;
        }
        return new string(chars);
    }

    public static PokerError MapValidation(ValidationResult value) => value switch
    {
        ValidationResult.CannotCheck => PokerError.CannotCheck,
        ValidationResult.RaiseTooSmall => PokerError.RaiseTooSmall,
        ValidationResult.RaiseTooLarge => PokerError.RaiseTooLarge,
        _ => PokerError.InvalidAction,
    };

    public static ActionResolution Resolve(PokerExecutionState state, DateTimeOffset now)
    {
        var table = state.Table!;
        var transition = PokerDomain.ResolveAfterAction(table, state.Seats);
        if (transition.Kind is not (TransitionKind.HandEndedLastStanding
            or TransitionKind.HandEndedRunout or TransitionKind.HandEndedShowdown))
        {
            var kind = transition.Kind == TransitionKind.PhaseAdvanced
                ? HandTransition.PhaseAdvanced : HandTransition.TurnAdvanced;
            return new(new ActionResult(PokerError.None, Snapshot(state), kind, null, null, null), [], []);
        }

        var showdown = transition.Showdown!.ToList();
        var reason = transition.Kind switch
        {
            TransitionKind.HandEndedLastStanding => "last_standing",
            TransitionKind.HandEndedRunout => "runout",
            _ => "showdown",
        };
        var payouts = showdown.Where(entry => entry.Won > 0 && entry.Seat.WagerBetId is null)
            .Select(entry => (IGameEffect)WalletEconomyEffect.Credit(
                entry.Seat.UserId, entry.Seat.ChatId, entry.Won, "poker.win"))
            .ToArray();
        var winners = showdown.Where(entry => entry.Won > 0)
            .Select(entry => new PokerPayout(entry.Seat.UserId, entry.Won)).ToArray();
        var payoutByUser = showdown.ToDictionary(entry => entry.Seat.UserId, entry => entry.Won);
        var wagerEvents = state.Seats
            .Where(seat => seat.WagerBetId is not null)
            .Select(seat => new PokerWagerOutcomeDeclared(
                seat.WagerBetId!,
                seat.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                payoutByUser.GetValueOrDefault(seat.UserId) > 0 ? "win" : "loss",
                JsonSerializer.Serialize(new { payout = payoutByUser.GetValueOrDefault(seat.UserId) }),
                now.ToUnixTimeMilliseconds()))
            .Cast<IDomainEvent>()
            .ToList();
        IDomainEvent[] events =
        [
            new PokerHandEnded(table.InviteCode, reason, winners, now.ToUnixTimeMilliseconds()),
            .. wagerEvents,
        ];
        return new(new ActionResult(PokerError.None, Snapshot(state), HandTransition.HandEnded,
            showdown, null, null), payouts, events);
    }

    public static TableSnapshot Snapshot(PokerExecutionState state) =>
        new(state.Table!, state.Seats);
}
