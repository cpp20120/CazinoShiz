namespace BotFramework.Contracts.Wagering;

/// <summary>Transport-neutral materialized status of one asynchronous wager.</summary>
public sealed record WagerOperation(
    string OperationId,
    string BetId,
    string GameId,
    string PlayerId,
    string GameInput,
    string TermsJson,
    WagerOperationStatus Status,
    string? OutcomeCode,
    string? ErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long? Payout = null);
