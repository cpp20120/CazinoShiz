// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public interface IAggregateRoot
{
    string Id { get; }
}
