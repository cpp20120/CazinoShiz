namespace BotFramework.Contracts.Operations;

public sealed record WagerWorkflowTimeline(
    string OperationId,
    string BetId,
    string GameId,
    string PlayerId,
    string WagerStatus,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WagerWorkflowStep> Steps);
