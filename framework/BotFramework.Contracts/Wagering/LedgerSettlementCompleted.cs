using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

public sealed record LedgerSettlementCompleted(string OperationId, string BetId, string PlayerId, bool Settled, string? ErrorCode, DateTimeOffset OccurredAt) : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "ledger.settlement.completed";
    public string? Topic => "ledger.events";
    public string? MessageKey => $"ledger:{PlayerId}";
}
