namespace BotFramework.Contracts.Risk;

/// <summary>Generic context passed to a risk policy owned by an application.</summary>
public sealed record RiskEvaluation<TContext>(
    string EvaluationId,
    string SubjectId,
    string OperationType,
    TContext Context,
    DateTimeOffset OccurredAt);
