// ─────────────────────────────────────────────────────────────────────────────
// Health checks — per-module liveness + readiness signals the Host aggregates
// at /health.
//
// Two kinds:
//   Liveness  — "is the process still reachable?" Used by the orchestrator
//               to decide whether to kill+restart. Almost always "ok" unless
//               the event loop is wedged; default to reporting healthy.
//   Readiness — "is this module ready to serve traffic?" Used by upstream
//               (webhook receiver, polling driver) to gate dispatches. A
//               module whose migrations haven't applied reports not-ready
//               until they do.
//
// Module contributes IHealthCheck instances via IModuleServiceCollection.
// Aggregation is WORST-WINS: if any module is unhealthy, /health returns 503
// with a JSON body listing which ones and why.
//
// Contract mirrors Microsoft.Extensions.Diagnostics.HealthChecks closely but
// doesn't reference it — framework modules shouldn't pull that dep just to
// report their status. Host adapter wraps between the two at the edge.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IHealthCheck
{
    /// Stable id used in the aggregated report. Typically "<moduleId>.<check>"
    /// — e.g. "poker.deck-rng-seed", "sh.event-store-reachable".
    string Name { get; }

    HealthCheckKind Kind { get; }

    Task<HealthStatus> CheckAsync(CancellationToken ct);
}

public enum HealthCheckKind { Liveness, Readiness }

public sealed record HealthStatus(bool Healthy, string? Detail = null)
{
    public static HealthStatus Ok { get; } = new(true);
    public static HealthStatus Degraded(string detail) => new(true, detail);
    public static HealthStatus Down(string detail) => new(false, detail);
}
