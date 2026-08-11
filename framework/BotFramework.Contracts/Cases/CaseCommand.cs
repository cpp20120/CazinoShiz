using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Cases;

public abstract record CaseCommand(
    string OperationId,
    string CaseId,
    DateTimeOffset OccurredAt)
    : IIntegrationCommand, IIntegrationMessageRouted
{
    public abstract string CommandType { get; }
    public string? Topic => "cases.commands";
    public string? MessageKey => $"case:{CaseId}";
}
