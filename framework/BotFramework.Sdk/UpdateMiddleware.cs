// ─────────────────────────────────────────────────────────────────────────────
// Update-pipeline middleware contract.
//
// Sits one level above per-command middleware (ICommandMiddleware). This layer
// is invoked once per incoming Telegram update — before any routing decisions
// or handler resolution has happened — and gets to wrap the whole dispatch:
// logging the raw update, translating exceptions, rate-limiting a user across
// all of their commands, etc.
//
// ASP.NET Core style: each middleware receives next() and decides whether to
// call it, short-circuit, wrap in try/catch, or transform the context.
//
//   update in  → [exception] → [logging] → [rate-limit] → router/handler
//   update out ← [exception] ← [logging] ← [rate-limit] ← router/handler
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public delegate Task UpdateDelegate(UpdateContext ctx);

public interface IUpdateMiddleware
{
    Task InvokeAsync(UpdateContext ctx, UpdateDelegate next);
}
