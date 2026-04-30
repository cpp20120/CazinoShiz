// ─────────────────────────────────────────────────────────────────────────────
// ClickHouseAnalyticsQueryService — read-side companion to
// ClickHouseAnalyticsService. Opens a short-lived connection per call so we
// don't share connection state with the buffered write path. Used by the
// admin /analytics command.
//
// Query strategy: every aggregation is filtered by project = '<project>' so
// distributions sharing one ClickHouse cluster see only their own slice.
//
// All values that flow into SQL are server-controlled (configuration project
// id, computed UTC timestamps, fixed integer top-N). No user-controlled data
// is interpolated, but project/database/table identifiers are still safely
// rebuilt with simple validation to keep the door closed against future
// config-injection regressions.
//
// Failure model: every public method either returns a populated result or
// throws — the handler is expected to render exceptions as a friendly
// "ClickHouse недоступен" message, not crash. Soft-disabled state (config
// off, host empty) is reported via GetStatusAsync and used by the handler
// to short-circuit before issuing queries.
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;

namespace BotFramework.Host.Services;

public interface IAnalyticsQueryService
{
    Task<AnalyticsServiceStatus> GetStatusAsync(CancellationToken ct);
    Task<AnalyticsReport> GetReportAsync(int topN, int timelineDays, CancellationToken ct);
}

public sealed class ClickHouseAnalyticsQueryService(
    IOptions<ClickHouseOptions> options) : IAnalyticsQueryService
{
    private readonly ClickHouseOptions _options = options.Value;

    public async Task<AnalyticsServiceStatus> GetStatusAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
            return new AnalyticsServiceStatus(Configured: false, Reachable: false,
                Error: "ClickHouse:Enabled = false");
        if (string.IsNullOrWhiteSpace(_options.Host))
            return new AnalyticsServiceStatus(Configured: false, Reachable: false,
                Error: "ClickHouse:Host is empty");

        try
        {
            await using var conn = OpenConnection();
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(ct);
            return new AnalyticsServiceStatus(Configured: true, Reachable: true, Error: null);
        }
        catch (Exception ex)
        {
            return new AnalyticsServiceStatus(Configured: true, Reachable: false, Error: ex.Message);
        }
    }

    public async Task<AnalyticsReport> GetReportAsync(int topN, int timelineDays, CancellationToken ct)
    {
        EnsureEnabled();
        if (topN <= 0) topN = 5;
        if (topN > 50) topN = 50;
        if (timelineDays <= 0) timelineDays = 14;
        if (timelineDays > 90) timelineDays = 90;

        await using var conn = OpenConnection();
        await conn.OpenAsync(ct);

        var qualifiedTable = QualifiedTable();
        var projectLiteral = QuoteString(_options.Project);

        var totalRows = await GetTotalRowsAsync(conn, qualifiedTable, projectLiteral, ct);

        var windows = new[]
        {
            ("24h", TimeSpan.FromHours(24)),
            ("7d",  TimeSpan.FromDays(7)),
            ("30d", TimeSpan.FromDays(30)),
        };

        var reports = new List<AnalyticsWindowReport>(windows.Length);
        foreach (var (label, span) in windows)
            reports.Add(await GetWindowReportAsync(conn, qualifiedTable, projectLiteral, label, span, topN, ct));

        var timeline = await GetTimelineAsync(conn, qualifiedTable, projectLiteral, timelineDays, ct);

        return new AnalyticsReport(
            GeneratedAtUtc: DateTime.UtcNow,
            Project: _options.Project,
            TableName: $"{_options.Database}.{_options.Table}",
            TotalRowsAllTime: totalRows,
            Windows: reports,
            Timeline: timeline);
    }

    private static async Task<long> GetTotalRowsAsync(
        ClickHouseConnection conn, string qualifiedTable, string projectLiteral, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT count() FROM {qualifiedTable}
            WHERE project = {projectLiteral}
            """;
        var raw = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(raw);
    }

    private async Task<AnalyticsWindowReport> GetWindowReportAsync(
        ClickHouseConnection conn, string qualifiedTable, string projectLiteral,
        string label, TimeSpan window, int topN, CancellationToken ct)
    {
        var sinceUtc = DateTime.UtcNow - window;
        var sinceLiteral = FormatDateTime64(sinceUtc);

        long total;
        long distinctUsers;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT count(),
                       uniqExactIf(user_id, user_id != 0)
                FROM {qualifiedTable}
                WHERE project = {projectLiteral}
                  AND created_at >= {sinceLiteral}
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            total = Convert.ToInt64(reader.GetValue(0));
            distinctUsers = Convert.ToInt64(reader.GetValue(1));
        }

        var topEvents = await ListGroupedAsync(
            conn, qualifiedTable, projectLiteral, "event_type", sinceLiteral, topN, ct);
        var topModules = await ListGroupedAsync(
            conn, qualifiedTable, projectLiteral, "module", sinceLiteral, topN, ct);
        var topUsers = await ListTopUsersAsync(
            conn, qualifiedTable, projectLiteral, sinceLiteral, topN, ct);

        return new AnalyticsWindowReport(
            Label: label,
            Window: window,
            TotalEvents: total,
            DistinctUsers: distinctUsers,
            TopEventTypes: topEvents,
            TopModules: topModules,
            TopUsers: topUsers);
    }

    private static async Task<IReadOnlyList<AnalyticsCount>> ListGroupedAsync(
        ClickHouseConnection conn, string qualifiedTable, string projectLiteral,
        string column, string sinceLiteral, int topN, CancellationToken ct)
    {
        // Whitelist `column` because identifiers cannot be parameterized.
        var safeColumn = column switch
        {
            "event_type" => "event_type",
            "module" => "module",
            _ => throw new ArgumentException($"unsupported column: {column}", nameof(column)),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {safeColumn} AS name, count() AS c
            FROM {qualifiedTable}
            WHERE project = {projectLiteral}
              AND created_at >= {sinceLiteral}
            GROUP BY {safeColumn}
            ORDER BY c DESC, {safeColumn} ASC
            LIMIT {topN}
            """;

        var rows = new List<AnalyticsCount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.IsDBNull(0) ? "" : Convert.ToString(reader.GetValue(0)) ?? "";
            var c = Convert.ToInt64(reader.GetValue(1));
            rows.Add(new AnalyticsCount(name, c));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<AnalyticsUserCount>> ListTopUsersAsync(
        ClickHouseConnection conn, string qualifiedTable, string projectLiteral,
        string sinceLiteral, int topN, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT user_id, count() AS c
            FROM {qualifiedTable}
            WHERE project = {projectLiteral}
              AND created_at >= {sinceLiteral}
              AND user_id != 0
            GROUP BY user_id
            ORDER BY c DESC, user_id ASC
            LIMIT {topN}
            """;

        var rows = new List<AnalyticsUserCount>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var userId = Convert.ToInt64(reader.GetValue(0));
            var c = Convert.ToInt64(reader.GetValue(1));
            rows.Add(new AnalyticsUserCount(userId, c));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<AnalyticsTimelineBucket>> GetTimelineAsync(
        ClickHouseConnection conn, string qualifiedTable, string projectLiteral,
        int days, CancellationToken ct)
    {
        var sinceDate = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var sinceLiteral = FormatDateTime64(sinceDate);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT toDate(created_at) AS d, count() AS c
            FROM {qualifiedTable}
            WHERE project = {projectLiteral}
              AND created_at >= {sinceLiteral}
            GROUP BY d
            ORDER BY d
            """;

        var byDay = new Dictionary<DateOnly, long>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var raw = reader.GetValue(0);
                var d = raw switch
                {
                    DateOnly dn => dn,
                    DateTime dt => DateOnly.FromDateTime(dt),
                    _ => DateOnly.FromDateTime(Convert.ToDateTime(raw, CultureInfo.InvariantCulture)),
                };
                byDay[d] = Convert.ToInt64(reader.GetValue(1));
            }
        }

        var buckets = new List<AnalyticsTimelineBucket>(days);
        for (var i = 0; i < days; i++)
        {
            var d = DateOnly.FromDateTime(sinceDate.AddDays(i));
            buckets.Add(new AnalyticsTimelineBucket(d, byDay.GetValueOrDefault(d, 0)));
        }
        return buckets;
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("ClickHouse analytics is disabled (ClickHouse:Enabled = false).");
        if (string.IsNullOrWhiteSpace(_options.Host))
            throw new InvalidOperationException("ClickHouse:Host is not configured.");
    }

    private string QualifiedTable() =>
        $"{QuoteIdentifier(_options.Database)}.{QuoteIdentifier(_options.Table)}";

    private static string QuoteIdentifier(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            throw new InvalidOperationException("ClickHouse identifier is empty.");
        // ClickHouse backtick-quoted identifier; escape backticks defensively.
        return "`" + raw.Replace("`", "``") + "`";
    }

    private static string QuoteString(string raw) =>
        "'" + raw.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string FormatDateTime64(DateTime utc)
    {
        var asUtc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
        return "toDateTime64('" +
               asUtc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) +
               "', 3, 'UTC')";
    }

    private ClickHouseConnection OpenConnection() =>
        new(BuildConnectionString(_options));

    private static string BuildConnectionString(ClickHouseOptions o)
    {
        // Mirrors ClickHouseAnalyticsService.BuildConnectionString — kept local
        // to avoid pulling that class's writer concerns into the read path.
        var parts = new List<string>();
        if (Uri.TryCreate(o.Host, UriKind.Absolute, out var uri))
        {
            parts.Add($"Host={uri.Host}");
            if (!uri.IsDefaultPort) parts.Add($"Port={uri.Port}");
            parts.Add($"Protocol={uri.Scheme}");
        }
        else
        {
            parts.Add($"Host={o.Host}");
        }
        parts.Add($"Username={o.User}");
        parts.Add($"Password={o.Password}");
        parts.Add($"Database={o.Database}");
        return string.Join(";", parts);
    }
}
