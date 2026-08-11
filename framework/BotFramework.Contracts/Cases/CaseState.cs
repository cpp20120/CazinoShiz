namespace BotFramework.Contracts.Cases;

public sealed record CaseState(
    string CaseId,
    string CaseType,
    string SubjectId,
    string OpenedBy,
    string Reason,
    CaseStatus Status,
    DateTimeOffset OpenedAt,
    IReadOnlyList<CaseEvidence> Evidence,
    string? ReviewerId = null,
    string? ResolutionCode = null,
    string? ResolutionNotes = null,
    string? ResolvedBy = null,
    DateTimeOffset? ResolvedAt = null,
    string? AppealReason = null,
    DateTimeOffset? AppealedAt = null,
    long Version = 0);
