# BotFramework

`framework/` is the reusable runtime layer for CasinoShiz modules.

| Assembly | Role | Referenced by |
|----------|------|---------------|
| `BotFramework.Sdk` | Contracts visible to modules: modules, handlers, repositories, events, projections, economics, analytics | every `Games.*` project |
| `BotFramework.Sdk.Testing` | xUnit helpers and in-memory test doubles | `tests/CasinoShiz.Tests` |
| `BotFramework.Host` | Infrastructure: ASP.NET Core host, Telegram, Postgres, Redis/CAP, ClickHouse, migrations, admin UI | `host/CasinoShiz.Host` |

Modules should depend on `BotFramework.Sdk` only. The Host composes modules through `builder.AddBotFramework().AddModule<T>()` and owns infrastructure wiring.

## Layering

```text
┌────────────────────────────────────────────────────────────────────────────┐
│ L4  Presentation  (games/*)                                                │
│     IUpdateHandler + [Command]/[CallbackPrefix]/[MessageDice]/[ChannelPost]│
│     IAdminPage                                                             │
├────────────────────────────────────────────────────────────────────────────┤
│ L3  Domain  (games/*/Domain)                                               │
│     IAggregateRoot / IEventSourcedAggregate / IDomainEvent                 │
│     pure domain transitions, policies and state machines                   │
├────────────────────────────────────────────────────────────────────────────┤
│ L2  Platform contracts  (BotFramework.Sdk)                                 │
│     IRepository<T>, IEventStore, IProjection, IDomainEventBus              │
│     IEconomicsService, IAnalyticsService, ILocalizer                       │
├────────────────────────────────────────────────────────────────────────────┤
│ L1  Infrastructure  (BotFramework.Host)                                    │
│     Postgres, Redis/CAP, ClickHouse, Telegram Bot API, ASP.NET Core        │
│     UpdateRouter, UpdatePipeline, migrations, admin UI                     │
└────────────────────────────────────────────────────────────────────────────┘
```

## Host composition

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddBotFramework()
    .AddModule<DiceModule>()
    .AddModule<PokerModule>()
    .AddModule<SecretHitlerModule>();

var app = builder.Build();
app.UseBotFramework();
app.Run();
```

`AddBotFramework()` registers framework services: Telegram client, update router, update pipeline, command bus, domain event bus, event-store stack, migrations, background jobs, analytics, admin UI and health endpoints.

`AddModule<T>()` creates the module, calls `ConfigureServices(IModuleServiceCollection)`, and collects module migrations, locales and bot commands.

## Update pipeline and routing

Every Telegram update flows through the update pipeline and then the attribute router:

```text
Exception → Deduplication → Logging → RateLimit → KnownChats → UpdateRouter
```

Routing is attribute-driven. A module adds a handler by implementing `IUpdateHandler`, decorating it with route attributes and registering it in `ConfigureServices`:

```csharp
services.AddHandler<MyHandler>();
```

Supported route attributes include `[Command]`, `[CallbackPrefix]`, `[CallbackFallback]`, `[MessageDice]` and `[ChannelPost]`.

## Persistence styles

The framework supports two aggregate persistence styles.

### Classical aggregates

Classical aggregates are persisted by module-owned stores/repositories, usually with Dapper and module-specific tables. Most simple game modules should use this style.

### Event-sourced aggregates

Event-sourced aggregates implement `IEventSourcedAggregate` and are registered by the module:

```csharp
services.RegisterAggregate<MyAggregate>(PersistenceStrategy.EventSourced);
```

This registration now wires:

- `IRepository<MyAggregate>` → `EventSourcedRepository<MyAggregate>`
- `IAggregateFactory<MyAggregate>` → `DefaultAggregateFactory<MyAggregate>` unless the module already registered a custom factory

The event-sourced repository owns the aggregate lifecycle:

1. Load stored events from `IEventStore`.
2. Deserialize and replay them through `LoadFromHistory`.
3. Append `PendingEvents` with optimistic concurrency.
4. Dispatch appended events through `EventDispatcher`.
5. Call `MarkEventsCommitted` after successful append.

`DefaultAggregateFactory<T>` tries to create aggregates through DI, first with the stream id as a `string` constructor argument and then without it. A module can override this by registering its own `IAggregateFactory<T>` before calling `RegisterAggregate<T>(PersistenceStrategy.EventSourced)`.

## Event flow

The current event-sourced flow is intentionally post-commit:

```text
Application service
  ↓
IRepository<TAggregate>
  ↓
EventSourcedRepository<TAggregate>
  ↓
IEventStore.AppendAsync(...)        // commits module_events
  ↓
EventDispatcher.DispatchAsync(...)  // projections, domain bus, analytics
```

`IEventStore` remains a small storage primitive: load stream and append events. It does not know about projections, subscribers or analytics.

`EventDispatcher` fans each committed event into:

- matching `IProjection`s;
- `IDomainEventBus` subscribers, including framework-wide subscribers;
- `IAnalyticsService.Track(...)`.

Post-commit dispatch means a dispatch failure does not roll back the already-committed event append. Projection handlers and subscribers should therefore be idempotent. Recovery should be done with replay/rebuild jobs or targeted retries.

`ProjectionContext.Transaction` is nullable. It is normally `null` in the current implementation. The field exists so a future same-transaction unit-of-work implementation can pass a provider-specific transaction object without changing module contracts.

## Cross-module domain events

`IDomainEventBus` lets modules react to events without referencing each other. Subscriptions are pattern-based:

| Pattern | Meaning |
|---------|---------|
| `sh.game_ended` | exact event |
| `sh.*` | every event from one module |
| `*.game_ended` | one action from any module |
| `*` | every event |

At startup, `EventSubscriptionInitializer` subscribes framework listeners to `*`:

- `EventLogSubscriber` writes events to `event_log` for admin/history views.
- `ClickHouseEventMirror` mirrors events to ClickHouse when analytics is enabled.

Modules add their own subscribers with:

```csharp
services.AddDomainEventSubscription<MySubscriber>("poker.*");
```

## Projections

Projections are module-owned read models. A projection declares which event types it handles:

```csharp
public sealed class MyProjection : IProjection
{
    public IReadOnlySet<string> SubscribedEventTypes { get; } =
        new HashSet<string> { "poker.hand_won" };

    public Task ApplyAsync(IDomainEvent ev, ProjectionContext ctx, CancellationToken ct)
    {
        // update read model table
    }
}
```

Register it in the module:

```csharp
services.AddProjection<MyProjection>();
```

Projections run after the event append commits. They should be idempotent and safe to replay.

## Event tables

`module_events` stores event-sourced aggregate streams. It is the source of truth for replayable aggregate state.

`event_log` is a flat audit/history stream populated by `EventLogSubscriber`. It can contain events from event-sourced modules and other published domain events. Admin history pages should read this table, not replay aggregate streams.

`module_snapshots` stores optional aggregate snapshots through `ISnapshotStore<T>`.

## Migrations

Framework-owned tables are created by framework migrations first. Module migrations are then applied through `IModuleMigrations` and tracked in `__module_migrations`.

Migrations are forward-only raw SQL migrations executed via Dapper.

## Adding a new event-sourced module

A new module that wants event sourcing should:

1. Define domain events implementing `IDomainEvent` with stable `EventType` strings, e.g. `mygame.round_started`.
2. Define an aggregate implementing `IEventSourcedAggregate`.
3. Register it with `RegisterAggregate<MyAggregate>(PersistenceStrategy.EventSourced)`.
4. Optionally register `IAggregateFactory<MyAggregate>` if the default DI/id constructor creation is not enough.
5. Register projections with `AddProjection<TProjection>()`.
6. Register cross-module subscribers with `AddDomainEventSubscription<TSubscriber>(pattern)`.

After that, appended aggregate events automatically flow through projections, event log, ClickHouse mirror, module subscribers and analytics.
