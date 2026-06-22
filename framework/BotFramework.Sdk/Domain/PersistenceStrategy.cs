// ─────────────────────────────────────────────────────────────────────────────
// IModule — the contract every game implements.
//
// A module is a self-contained feature: its DI registrations, entity
// configurations, locales, route handlers, and config options all live here.
// Adding a game to a Host = reference the module's assembly + register it once.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public enum PersistenceStrategy
{
    /// Aggregate is mutated in place and persisted as a row. Default.
    Classical,
    /// Aggregate emits domain events which are appended to an event store.
    /// State is rebuilt by replaying events. Opt in per aggregate.
    EventSourced,
}
