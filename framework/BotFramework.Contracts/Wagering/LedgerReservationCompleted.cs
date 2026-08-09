using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Ledger response to a reservation. No game rules are present.</summary>
public sealed record LedgerReservationCompleted(string OperationId, string BetId, string PlayerId, bool Reserved, string? RejectionCode, DateTimeOffset OccurredAt) : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "ledger.reservation.completed";
    public string? Topic => "ledger.events";
    public string? MessageKey => $"ledger:{PlayerId}";
}
