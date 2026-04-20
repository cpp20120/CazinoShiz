// ─────────────────────────────────────────────────────────────────────────────
// HealthEndpoint — aggregates every module's IHealthCheck into a single
// /health response.
//
// Response shape:
//   200 {"status":"ok","checks":[...]}
//   503 {"status":"down","checks":[{"name":"sh.event-store","healthy":false,"detail":"..."}]}
//
// Kubernetes / Docker-Compose / whatever wants two endpoints:
//   /health/live     — only liveness checks; almost always ok
//   /health/ready    — liveness AND readiness; false during startup
//
// Checks run in parallel with Task.WhenAll. A single slow check doesn't
// serialize the others. Overall latency is bounded by the slowest check;
// modules are expected to keep their checks under ~1s.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed class HealthEndpoint(IEnumerable<IHealthCheck> checks)
{
    private readonly IReadOnlyList<IHealthCheck> _checks = checks.ToList();

    public async Task<HealthReport> RunAsync(HealthCheckKind? kindFilter, CancellationToken ct)
    {
        var selected = kindFilter is null
            ? _checks
            : _checks.Where(c => c.Kind == kindFilter).ToList();

        var results = await Task.WhenAll(selected.Select(async check =>
        {
            try
            {
                var status = await check.CheckAsync(ct);
                return new HealthReportItem(check.Name, check.Kind, status.Healthy, status.Detail);
            }
            catch (Exception ex)
            {
                return new HealthReportItem(check.Name, check.Kind, Healthy: false, Detail: ex.Message);
            }
        }));

        var healthy = results.All(r => r.Healthy);
        return new HealthReport(healthy, results);
    }
}

public sealed record HealthReport(bool Healthy, IReadOnlyList<HealthReportItem> Checks);
public sealed record HealthReportItem(string Name, HealthCheckKind Kind, bool Healthy, string? Detail);
