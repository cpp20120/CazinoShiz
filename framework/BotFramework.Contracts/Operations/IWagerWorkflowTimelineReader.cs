namespace BotFramework.Contracts.Operations;

public interface IWagerWorkflowTimelineReader
{
    Task<WagerWorkflowTimeline?> GetAsync(string operationId, CancellationToken ct);
}
