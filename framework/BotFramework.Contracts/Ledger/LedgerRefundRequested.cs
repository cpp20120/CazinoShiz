namespace BotFramework.Contracts.Ledger;

/// <summary>
/// Refunds an already captured amount. It is intentionally distinct from
/// Release, which can only act on the uncaptured part of a hold.
/// </summary>
public sealed record LedgerRefundRequested(
    string OperationId,
    string OriginalOperationId,
    string AccountId,
    long Amount,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.refund.requested";
    public override string? MessageKey => $"ledger:{AccountId}";
}
