using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Cases;

public abstract record CaseEvent(string CaseId, DateTimeOffset OccurredAt)
    : IIntegrationEvent, IIntegrationMessageRouted
{
    public abstract string EventType { get; }
    public string? Topic => "cases.events";
    public string? MessageKey => $"case:{CaseId}";
}

public sealed record CaseOpened(CaseState Case)
    : CaseEvent(Case.CaseId, Case.OpenedAt)
{
    public override string EventType => "case.opened";
}

public sealed record CaseEvidenceAdded(string CaseId, CaseEvidence Evidence, DateTimeOffset OccurredAt)
    : CaseEvent(CaseId, OccurredAt)
{
    public override string EventType => "case.evidence.added";
}

public sealed record CaseReviewStarted(string CaseId, string ReviewerId, DateTimeOffset OccurredAt)
    : CaseEvent(CaseId, OccurredAt)
{
    public override string EventType => "case.review.started";
}

public sealed record CaseResolved(
    string CaseId,
    string ResolverId,
    string ResolutionCode,
    DateTimeOffset OccurredAt)
    : CaseEvent(CaseId, OccurredAt)
{
    public override string EventType => "case.resolved";
}

public sealed record CaseAppealed(string CaseId, string Reason, DateTimeOffset OccurredAt)
    : CaseEvent(CaseId, OccurredAt)
{
    public override string EventType => "case.appealed";
}
