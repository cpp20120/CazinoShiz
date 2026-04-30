// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsQueryResults — DTOs returned by IAnalyticsQueryService.
//
// Kept separate from ClickHouseAnalyticsService (write path) so handlers that
// only need to read can depend on the lightweight query interface without
// pulling in the buffered-flush hosted service surface.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Host.Services;

public sealed record AnalyticsCount(string Name, long Count);

public sealed record AnalyticsUserCount(long UserId, long Count);

public sealed record AnalyticsTimelineBucket(DateOnly Day, long Count);

public sealed record AnalyticsWindowReport(
    string Label,
    TimeSpan Window,
    long TotalEvents,
    long DistinctUsers,
    IReadOnlyList<AnalyticsCount> TopEventTypes,
    IReadOnlyList<AnalyticsCount> TopModules,
    IReadOnlyList<AnalyticsUserCount> TopUsers);

public sealed record AnalyticsReport(
    DateTime GeneratedAtUtc,
    string Project,
    string TableName,
    long TotalRowsAllTime,
    IReadOnlyList<AnalyticsWindowReport> Windows,
    IReadOnlyList<AnalyticsTimelineBucket> Timeline);

public sealed record AnalyticsServiceStatus(
    bool Configured,
    bool Reachable,
    string? Error);
