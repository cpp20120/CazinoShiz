namespace BotFramework.Contracts.Ledger;

/// <summary>Moves funds between two ledger accounts atomically.</summary>
public sealed record LedgerTransferRequested(
    string OperationId,
    string FromAccountId,
    string ToAccountId,
    long Amount,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt)
    : LedgerCommand(OperationId, Currency, Reason, OccurredAt)
{
    public override string CommandType => "ledger.transfer.requested";
    public override string? MessageKey => $"ledger:{FromAccountId}";
}
