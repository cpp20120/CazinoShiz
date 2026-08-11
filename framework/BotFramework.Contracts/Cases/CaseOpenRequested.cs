namespace BotFramework.Contracts.Cases;

public sealed record CaseOpenRequested(
    string OperationId,
    string CaseId,
    string CaseType,
    string SubjectId,
    string OpenedBy,
    string Reason,
    DateTimeOffset OccurredAt)
    : CaseCommand(OperationId, CaseId, OccurredAt)
{
    public override string CommandType => "case.open.requested";
}
