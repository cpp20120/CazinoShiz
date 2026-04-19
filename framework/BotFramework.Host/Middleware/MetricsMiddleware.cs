// ─────────────────────────────────────────────────────────────────────────────
// MetricsMiddleware — counters + histogram per (module, command, outcome).
//
// Sits between logging and everything else: far enough out to see the full
// latency, close enough in to not skew counts by auth rejections the caller
// doesn't own. Emits through the platform IMetrics abstraction so backend
// is pluggable (Prometheus, OpenTelemetry, stdout).
//
// Metric names follow a stable shape:
//   bot_commands_total{module="poker", command="JoinCommand", outcome="ok"}
//   bot_command_duration_ms{module="poker", command="JoinCommand"}
//
// No per-user dimensions — cardinality would explode. User-level analysis
// is what ClickHouse is for; Prometheus-style metrics stay aggregated.
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using BotFramework.Sdk;

namespace BotFramework.Host.Middleware;

public sealed class MetricsMiddleware(IMetrics metrics) : ICommandMiddleware
{
    public async Task InvokeAsync(CommandContext ctx, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();
        var commandType = ctx.Command.GetType().Name;
        var outcome = "ok";

        try
        {
            await next();
        }
        catch
        {
            outcome = "error";
            throw;
        }
        finally
        {
            metrics.CounterInc("bot_commands_total", new Dictionary<string, string>
            {
                ["module"] = ctx.Command.ModuleId,
                ["command"] = commandType,
                ["outcome"] = outcome,
            });
            metrics.HistogramObserve("bot_command_duration_ms", sw.ElapsedMilliseconds, new Dictionary<string, string>
            {
                ["module"] = ctx.Command.ModuleId,
                ["command"] = commandType,
            });
        }
    }
}
