using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Cases;

public sealed record CaseTransitionRejected(
    string OperationId,
    string CaseId,
    string ErrorCode,
    DateTimeOffset OccurredAt)
    : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "case.transition.rejected";
    public string? Topic => "cases.events";
    public string? MessageKey => $"case:{CaseId}";
}
