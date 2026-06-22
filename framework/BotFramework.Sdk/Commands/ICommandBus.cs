// ─────────────────────────────────────────────────────────────────────────────
// CQRS command layer — the platform-level dispatch pipeline.
//
// Why add a command bus on top of application services:
//
//   Without it: a handler calls IPokerService.JoinAsync directly. Rate limit?
//   Log line? Metric? Every handler grows its own copy of that plumbing.
//   Cross-cutting concerns diffuse into every handler → drift, inconsistency,
//   and rate-limit bugs that surface only after something has already gone
//   wrong.
//
//   With it: a handler builds a PokerJoinCommand and hands it to ICommandBus.
//   The bus runs every registered ICommandMiddleware (logging → metrics →
//   auth → rate-limit → validation → handler). A new cross-cutting concern is
//   one new middleware registration, not a sweep across 40 handlers.
//
// Pattern is classic mediator; the twist here is that *modules* contribute
// command handlers, so the bus ends up per-module-aware. Logging middleware
// tags every log line with the owning module. Rate-limit middleware scopes
// its bucket per (user, command-type) so noisy-neighbor games don't starve
// quiet ones.
//
// Contracts live in the SDK so both module authors (for handler registration)
// and Host authors (for middleware registration) share the same vocabulary.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Entry point for modules. A handler that wants to dispatch a command
/// resolves ICommandBus from DI and calls Send. Every middleware in the
/// configured pipeline runs around the handler — logs, metrics, auth,
/// rate-limit — before and after the terminal handler executes.
public interface ICommandBus
{
    Task SendAsync(ICommand command, CancellationToken ct);
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct);
}
