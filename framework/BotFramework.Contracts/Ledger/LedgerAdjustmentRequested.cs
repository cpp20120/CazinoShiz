namespace BotFramework.Contracts.Ledger;

/// <summary>
/// Applies an audited signed balance correction. This is an administrative
/// primitive and must be protected by the caller's authorization boundary.
/// </summary>
public sealed record LedgerAdjustmentRequested(
    string OperationId,
    string AccountId,
    long Delta,
    string Currency,
    string Reason,
    string ActorId,
    DateTimeOffset OccurredAt)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.adjustment.requested";
    public override string? MessageKey => $"ledger:{AccountId}";
}
