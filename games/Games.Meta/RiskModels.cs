namespace Games.Meta;

public sealed record RiskFlag(
    long Id,
    long SeasonId,
    long ChatId,
    long UserId,
    string DisplayName,
    string Kind,
    string Severity,
    string Status,
    string Reason,
    string EvidenceJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record RiskFlagView(
    long Id,
    long ChatId,
    long UserId,
    string DisplayName,
    string Kind,
    string Severity,
    string Status,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record RiskResolveResult(bool Updated, string Message);
