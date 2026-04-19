// ─────────────────────────────────────────────────────────────────────────────
// LoggingMiddleware — one log line per command, with timing + outcome.
//
// Placed OUTERMOST in the pipeline so it observes everything: auth rejections,
// rate-limit rejections, validation failures, unhandled exceptions. Structure
// the line so operators can pivot by module_id + command_type + outcome in
// Grafana without grep.
//
// Trade-off: every command produces a log line whether it succeeded or not.
// For noisy commands (e.g. /roll under load) this becomes chatty. Operators
// turn the verbosity down per module-id through standard ILogger filters;
// the framework doesn't add its own level config on top.
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using BotFramework.Sdk;
using Microsoft.Extensions.Logging;

namespace BotFramework.Host.Middleware;

public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> log) : ICommandMiddleware
{
    public async Task InvokeAsync(CommandContext ctx, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();
        var commandType = ctx.Command.GetType().Name;

        try
        {
            await next();
            log.LogInformation(
                "cmd module={ModuleId} type={CommandType} user={UserId} trace={TraceId} ms={Ms} outcome=ok",
                ctx.Command.ModuleId, commandType, ctx.Request.UserId, ctx.Request.TraceId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "cmd module={ModuleId} type={CommandType} user={UserId} trace={TraceId} ms={Ms} outcome=error",
                ctx.Command.ModuleId, commandType, ctx.Request.UserId, ctx.Request.TraceId, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
