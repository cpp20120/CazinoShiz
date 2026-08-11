namespace BotFramework.Contracts.Cases;

public sealed record CaseAppealRequested(
    string OperationId,
    string CaseId,
    string Reason,
    DateTimeOffset OccurredAt)
    : CaseCommand(OperationId, CaseId, OccurredAt)
{
    public override string CommandType => "case.appeal.requested";
}
