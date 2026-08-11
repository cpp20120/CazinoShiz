namespace BotFramework.Contracts.Operations;

public sealed record WagerWorkflowStep(
    string Source,
    string Step,
    string Status,
    string? OperationId,
    long? Version,
    DateTimeOffset OccurredAt,
    string? DetailsJson);
