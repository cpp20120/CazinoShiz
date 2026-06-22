// ─────────────────────────────────────────────────────────────────────────────
// Aggregate + persistence abstractions.
//
// Application services depend on IRepository<T>. The concrete repo might be
// EF-backed (classical) or event-store-backed (event-sourced) — services don't
// care. That's the whole point: persistence choice is a module decision, not a
// service decision.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Creates aggregate instances during repository load. Modules may register a
/// custom factory when an aggregate needs construction policy beyond a public
/// parameterless or string-id constructor.
public interface IAggregateFactory<out TAggregate>
    where TAggregate : IAggregateRoot
{
    TAggregate Create(string id);
}
