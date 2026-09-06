namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>Money already debited while waiting for an external outcome.</summary>
public sealed record DeferredOutcomeWager(
    long UserId,
    long ChatId,
    long Amount,
    DateTimeOffset PlacedAt);
