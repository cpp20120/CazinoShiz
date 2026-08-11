using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Ledger;

/// <summary>
/// Common result event for every ledger primitive. Consumers should use the
/// operation id and status rather than assuming delivery is exactly once.
/// </summary>
public sealed record LedgerOperationCompleted(
    string OperationId,
    LedgerOperationKind OperationKind,
    LedgerOperationStatus Status,
    bool Succeeded,
    string? AccountId,
    string? ReferenceId,
    long AppliedAmount,
    long? RemainingAmount,
    string Currency,
    string? ErrorCode,
    DateTimeOffset OccurredAt)
    : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "ledger.operation.completed";
    public string? Topic => "ledger.events";
    public string? MessageKey => $"ledger:{AccountId ?? ReferenceId ?? OperationId}";
}
