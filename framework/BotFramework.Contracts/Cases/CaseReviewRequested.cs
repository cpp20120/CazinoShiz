namespace BotFramework.Contracts.Cases;

public sealed record CaseReviewRequested(
    string OperationId,
    string CaseId,
    string ReviewerId,
    DateTimeOffset OccurredAt)
    : CaseCommand(OperationId, CaseId, OccurredAt)
{
    public override string CommandType => "case.review.requested";
}
