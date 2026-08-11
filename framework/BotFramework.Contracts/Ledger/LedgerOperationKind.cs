namespace BotFramework.Contracts.Ledger;

/// <summary>
/// A transport-neutral financial operation understood by the framework.
/// Implementations own the actual balance and accounting invariants.
/// </summary>
public enum LedgerOperationKind
{
    Hold,
    Capture,
    Release,
    Refund,
    Transfer,
    Adjustment,
}
