using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Wagering-owned terminal fact published after Ledger confirms settlement.</summary>
public sealed record WagerSettled(
    string OperationId,
    string BetId,
    string PlayerId,
    WagerOperationStatus Status,
    string OutcomeCode,
    DateTimeOffset OccurredAt) : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "wager.settled";
    public string? Topic => "wagering.events";
    public string? MessageKey => $"wager:{PlayerId}";
}
