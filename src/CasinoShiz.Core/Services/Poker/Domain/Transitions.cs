using CasinoShiz.Data.Entities;

namespace CasinoShiz.Services.Poker.Domain;

public enum ValidationResult
{
    Ok = 0,
    CannotCheck,
    RaiseTooSmall,
    RaiseTooLarge,
    Invalid,
}

public enum TransitionKind
{
    TurnAdvanced,
    PhaseAdvanced,
    HandEndedLastStanding,
    HandEndedRunout,
    HandEndedShowdown,
}

public sealed record Transition(
    TransitionKind Kind,
    PokerPhase FromPhase,
    PokerPhase ToPhase,
    IReadOnlyList<ShowdownEntry>? Showdown = null);

public sealed record ShowdownEntry(PokerSeat Seat, HandRank? Rank, int Won, string HoleCards);
