using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Wagering-authorized settlement; Ledger applies it idempotently by operation id.</summary>
public sealed record LedgerSettlementRequested(string OperationId, string BetId, string PlayerId, long Payout, string Currency, DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "ledger.settlement.requested";
    public string? Topic => "ledger.commands";
    public string? MessageKey => $"ledger:{PlayerId}";
}
