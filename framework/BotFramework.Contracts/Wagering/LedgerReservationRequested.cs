using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Wagering asks Ledger to reserve funds; Ledger alone enforces balance invariants.</summary>
public sealed record LedgerReservationRequested(string OperationId, string BetId, string PlayerId, long Amount, string Currency, DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "ledger.reservation.requested";
    public string? Topic => "ledger.commands";
    public string? MessageKey => $"ledger:{PlayerId}";
}
