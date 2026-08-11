using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Ledger;

/// <summary>
/// Base for durable ledger commands. OperationId is the idempotency key and
/// must remain stable across retries and transport changes.
/// </summary>
public abstract record LedgerCommand(
    string OperationId,
    string Currency,
    string Reason,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public abstract string CommandType { get; }

    public string? Topic => "ledger.commands";

    public virtual string? MessageKey => $"ledger:{OperationId}";
}
