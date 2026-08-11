namespace BotFramework.Contracts.Cases;

public sealed record CaseEvidenceRequested(
    string OperationId,
    string CaseId,
    CaseEvidence Evidence,
    DateTimeOffset OccurredAt)
    : CaseCommand(OperationId, CaseId, OccurredAt)
{
    public override string CommandType => "case.evidence.requested";
}
