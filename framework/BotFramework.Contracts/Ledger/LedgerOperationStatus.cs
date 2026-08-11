namespace BotFramework.Contracts.Ledger;

public enum LedgerOperationStatus
{
    Processing,
    Held,
    PartiallyCaptured,
    Captured,
    PartiallyReleased,
    Released,
    PartiallyRefunded,
    Refunded,
    Completed,
    Rejected,
    Failed,
}
