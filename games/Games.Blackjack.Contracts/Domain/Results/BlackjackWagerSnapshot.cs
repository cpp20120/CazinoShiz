using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Contracts.Domain.Results;

/// <summary>
/// Game-only snapshot for the outcome-only mode. Financial fields intentionally
/// live in Wagering and are not part of this contract.
/// </summary>
public sealed record BlackjackWagerSnapshot(
    string BetId,
    string[] PlayerCards,
    string[] DealerCards,
    int PlayerTotal,
    int DealerTotal,
    bool DealerHoleRevealed,
    bool CanHit,
    BlackjackOutcome? Outcome,
    int? StateMessageId);
