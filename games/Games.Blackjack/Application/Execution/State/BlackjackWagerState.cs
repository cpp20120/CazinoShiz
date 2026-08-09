using BotFramework.Sdk.Execution;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

/// <summary>
/// Blackjack aggregate for the outcome-only wagering mode.
///
/// BetId links the game aggregate back to Wagering. Stake, wallet balance and
/// payout are deliberately absent; the game only owns cards and outcome.
/// </summary>
public sealed record BlackjackWagerState(
    long Revision,
    string BetId,
    string PlayerId,
    long ChatId,
    string DisplayName,
    TurnGameStatus Status,
    string CurrentPlayerId,
    DateTimeOffset? TurnDeadline,
    string[] PlayerCards,
    string[] DealerCards,
    string DeckState,
    int? StateMessageId,
    BlackjackOutcome? Outcome,
    string? OutcomeEvidence) : IVersionedGameState;
