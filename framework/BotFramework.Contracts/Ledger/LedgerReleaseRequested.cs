namespace BotFramework.Contracts.Ledger;

/// <summary>Releases all or part of a hold back to the available balance.</summary>
public sealed record LedgerReleaseRequested(
    string OperationId,
    string HoldId,
    string AccountId,
    long Amount,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.release.requested";
    public override string? MessageKey => $"ledger:{AccountId}";
}
