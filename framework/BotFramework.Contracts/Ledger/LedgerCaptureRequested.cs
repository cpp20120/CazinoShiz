namespace BotFramework.Contracts.Ledger;

/// <summary>
/// Captures all or part of a previously placed hold. Capture does not create
/// a payout; use a separate transfer for that.
/// </summary>
public sealed record LedgerCaptureRequested(
    string OperationId,
    string HoldId,
    string AccountId,
    long Amount,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt,
    string? DestinationAccountId = null)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.capture.requested";
    public override string? MessageKey => $"ledger:{AccountId}";
}
