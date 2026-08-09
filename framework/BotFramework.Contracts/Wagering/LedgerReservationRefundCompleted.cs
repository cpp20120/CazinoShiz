using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

public sealed record LedgerReservationRefundCompleted(
    string WorkflowId,
    string OperationId,
    string BetId,
    string PlayerId,
    bool Refunded,
    string? ErrorCode,
    DateTimeOffset OccurredAt) : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "ledger.reservation.refund.completed";
    public string? Topic => "ledger.events";
    public string? MessageKey => $"ledger:{PlayerId}";
}
