using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Compensates a previously reserved wager after group failure.</summary>
public sealed record LedgerReservationRefundRequested(
    string WorkflowId,
    string OperationId,
    string BetId,
    string PlayerId,
    long Amount,
    string Currency,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "ledger.reservation.refund.requested";
    public string? Topic => "ledger.commands";
    public string? MessageKey => $"ledger:{PlayerId}";
}
