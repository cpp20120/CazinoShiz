namespace BotFramework.Contracts.Cases;

public sealed record CaseResolveRequested(
    string OperationId,
    string CaseId,
    string ResolverId,
    string ResolutionCode,
    string? Notes,
    DateTimeOffset OccurredAt)
    : CaseCommand(OperationId, CaseId, OccurredAt)
{
    public override string CommandType => "case.resolve.requested";
}
