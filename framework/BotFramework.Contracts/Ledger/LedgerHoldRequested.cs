namespace BotFramework.Contracts.Ledger;

/// <summary>Places funds on hold. A hold may later be captured or released.</summary>
public sealed record LedgerHoldRequested(
    string OperationId,
    string HoldId,
    string AccountId,
    long Amount,
    string Currency,
    DateTimeOffset ExpiresAt,
    string Reason,
    DateTimeOffset OccurredAt)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.hold.requested";
    public override string? MessageKey => $"ledger:{AccountId}";
}
