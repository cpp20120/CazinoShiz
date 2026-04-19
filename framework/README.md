# BotFramework

The live framework under `framework/` is split into three assemblies:

| Assembly | Role | Referenced by |
|----------|------|---------------|
| `BotFramework.Sdk` | Contracts every module sees | every `Games.*` project |
| `BotFramework.Sdk.Testing` | xUnit helpers (in-memory stubs) | `tests/CasinoShiz.Tests` |
| `BotFramework.Host` | The one infrastructure assembly — Postgres, ClickHouse, Telegram client, ASP.NET hosting, admin mount | `host/CasinoShiz.Host` |

Modules only reference `BotFramework.Sdk`. The Host never references a specific game — it only references SDK and is composed with `AddModule<T>()`. That's the invariant that makes a module portable to another Host distribution (e.g. a party-games bot): same module project, different `Program.cs`.

## Layering

```
┌────────────────────────────────────────────────────────────────────────────┐
│ L4  Presentation  (in games/*/)                                            │
│     IUpdateHandler + [Command]/[CallbackPrefix]/[MessageDice]/[ChannelPost]│
│     IAdminPage — module-contributed admin Razor pages                      │
├────────────────────────────────────────────────────────────────────────────┤
│ L3  Domain  (in games/*/Domain/)                                           │
│     IAggregateRoot / IEventSourcedAggregate / IDomainEvent                 │
│     Pure: Deck, HandEvaluator, Transitions, policy decks, role dealers     │
├────────────────────────────────────────────────────────────────────────────┤
│ L2  Platform  (BotFramework.Sdk)                                           │
│     IDomainEventBus (pattern-matched, in-process)                          │
│     IRepository<T> · IEventStore · ISnapshotStore<T>                       │
│     IEconomicsService · IAnalyticsService · ILocalizer                     │
│     INpgsqlConnectionFactory · IModuleMigrations · IModuleServiceCollection│
│     ICommandBus + ICommandMiddleware (opt-in CQRS)                         │
│     IHealthCheck · IMetrics · IFeatureFlags                                │
├────────────────────────────────────────────────────────────────────────────┤
│ L1  Infrastructure  (BotFramework.Host)                                    │
│     Postgres · ClickHouse · Telegram Bot API · ASP.NET Core                │
│     PostgresEventStore · PostgresSnapshotStore · PostgresEventLog          │
│     ClickHouseAnalyticsService · EconomicsService                          │
│     UpdateRouter · UpdatePipeline · BotHostedService · AdminMount          │
└────────────────────────────────────────────────────────────────────────────┘
```

## Layout

```
framework/
├── BotFramework.Sdk/                          contracts assembly (modules reference this)
│   ├── IModule.cs                                   IModule + IModuleServiceCollection
│   ├── UpdateHandling.cs                            IUpdateHandler + route attributes
│   │                                                ([Command], [CallbackPrefix], [CallbackFallback],
│   │                                                 [MessageDice], [ChannelPost])
│   ├── UpdateMiddleware.cs                          IUpdateMiddleware + UpdateContext
│   ├── Aggregates.cs                                IAggregateRoot / IEventSourcedAggregate /
│   │                                                IDomainEvent / IRepository<T> / IEventStore
│   ├── Snapshots.cs                                 ISnapshotable / ISnapshotStore<T>
│   ├── Projections.cs                               IProjection / IRebuildableProjection
│   ├── ModuleMigrations.cs                          IModuleMigrations / IMigration
│   ├── AdminContribution.cs                         IAdminPage
│   ├── Commands.cs                                  ICommand / ICommandHandler / ICommandBus
│   ├── Pipeline.cs                                  ICommandMiddleware / CommandContext
│   ├── DomainEventBus.cs                            IDomainEventBus / IDomainEventSubscriber
│   ├── HealthChecks.cs                              IHealthCheck (liveness / readiness)
│   ├── FeatureFlags.cs                              IFeatureFlags
│   └── Metrics.cs                                   IMetrics
│
├── BotFramework.Sdk.Testing/                  xUnit helpers
│   └── Testing.cs                                   InMemoryRepository<T>, FakeEconomicsService
│
└── BotFramework.Host/                         the one infra assembly
    ├── BotFrameworkBuilder.cs                       builder.AddBotFramework().AddModule<T>()
    ├── BotFrameworkApplicationExtensions.cs         app.UseBotFramework() — webhook/health/admin endpoints
    ├── BotFrameworkOptions.cs                       "Bot" config section
    ├── ModuleServiceCollectionAdapter.cs            narrow IModuleServiceCollection view over IServiceCollection
    ├── ModuleLoader.cs                              LoadedModules snapshot after AddModule calls
    │
    ├── BotHostedService.cs                          long-polling entry (dev); webhook entry lives in ApplicationExtensions
    ├── UpdateRouter.cs                              attribute-scanning dispatch across every loaded handler
    ├── Pipeline/                                    update pipeline (outermost → innermost)
    │   ├── UpdatePipeline.cs                            composition
    │   ├── ExceptionMiddleware.cs                       catches + tracks "_framework.error" to ClickHouse
    │   ├── LoggingMiddleware.cs                         structured log line per update
    │   └── RateLimitMiddleware.cs                       per-user token bucket
    │
    ├── CommandBus.cs                                reflective command dispatch (opt-in CQRS)
    ├── Middleware/                                  command-bus middleware (distinct from Pipeline/)
    │   ├── LoggingMiddleware.cs
    │   ├── MetricsMiddleware.cs
    │   └── RateLimitMiddleware.cs
    │
    ├── InProcessEventBus.cs                         pattern-matched subscription ("sh.*", "*.game_ended", "*")
    ├── EventSubscriptionInitializer.cs              hydrates the bus at startup; wires "*" framework subscribers first
    ├── EventDispatcher.cs                           fans aggregate events from the store into projections+subscribers
    ├── PostgresEventStore.cs                        IEventStore over module_events (optimistic concurrency by stream version)
    ├── PostgresSnapshotStore.cs                     ISnapshotStore<T> over module_snapshots
    ├── EventLog.cs                                  PostgresEventLog + EventLogSubscriber — appends every domain event
    │                                                into event_log for admin history (separate from module_events,
    │                                                which only stores event-sourced-aggregate events)
    ├── EfRepository.cs                              IRepository<T> classical (Dapper-based; name is legacy)
    ├── JsonEventSerializer.cs                       System.Text.Json polymorphic serializer for IDomainEvent
    │
    ├── NpgsqlConnectionFactory.cs                   INpgsqlConnectionFactory over a pooled NpgsqlDataSource
    ├── DapperTypeHandlers.cs                        JSON / enum / DateTimeOffset handlers
    │
    ├── Localizer.cs                                 module-scoped ILocalizer ("moduleId.key" → value)
    ├── HostServices.cs                              small helper plumbing
    │
    ├── FrameworkMigrations.cs                       framework-owned tables:
    │                                                __module_migrations, users, economics ledger,
    │                                                module_events, module_snapshots, event_log
    ├── ModuleMigrationRunner.cs                     applies framework + module migrations before any service starts
    ├── BackgroundJobRunner.cs                       IHostedService shell for IBackgroundJob instances
    │
    ├── AdminMount.cs                                /admin gate — AdminWebToken, then module page mount
    ├── HealthEndpoint.cs                            aggregates IHealthCheck at /health
    │
    └── Services/                                    cross-cutting services modules inject
        ├── EconomicsService.cs                          IEconomicsService — shared ledger
        ├── InsufficientFundsException.cs
        ├── TaxService.cs
        ├── CaptchaService.cs
        ├── ClickHouseOptions.cs                         "ClickHouse" config section
        ├── ClickHouseAnalyticsService.cs                IAnalyticsService — bulk-copy writer, events_v2 table
        ├── ClickHouseEventMirror.cs                     IDomainEventSubscriber("*") — forwards every event to ClickHouse
        └── Random/                                      deterministic random sources for tests
```

## Host composition

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddBotFramework()
    .AddModule<DiceModule>()
    .AddModule<PokerModule>()
    .AddModule<SecretHitlerModule>()
    // …

var app = builder.Build();
app.UseBotFramework();
app.Run();
```

- `AddBotFramework()` registers framework singletons: `ITelegramBotClient`, `UpdateRouter`, `UpdatePipeline`, the three default update middlewares, `ICommandBus`, `IDomainEventBus`, Postgres + event-store stack, ClickHouse analytics, `EventSubscriptionInitializer`, `ModuleMigrationRunner`, `BackgroundJobRunner`, `BotHostedService`.
- `AddModule<T>()` new-ups the module (parameterless ctor), lets it run `ConfigureServices(IModuleServiceCollection)` against the shared container, and folds its locales / bot commands / migrations / admin pages into a builder-local aggregate. Order of `AddModule` calls = load order.
- `UseBotFramework()` maps the webhook endpoint (`POST /{botToken}`), the health endpoint, and the admin routes. Razor Pages from module-owned admin pages get mounted here.

Two update entry paths both funnel through `UpdateRouter`:

1. **Polling** (dev — `Bot:IsProduction=false`): `BotHostedService` long-polls `GetUpdates` and dispatches in a fresh DI scope.
2. **Webhook** (prod — `Bot:IsProduction=true`): `POST /{botToken}` reads the JSON body and calls the same router.

## Update pipeline and routing

Every update flows through `UpdatePipeline`:

```
ExceptionMiddleware  →  LoggingMiddleware  →  RateLimitMiddleware  →  UpdateRouter
```

Routing is **attribute-driven**. At startup `UpdateRouter` reflects over every `IUpdateHandler` registered by any module and picks the first whose `RouteAttribute` matches. Priorities live in `Sdk/UpdateHandling.cs`:

| Attribute | Priority | Purpose |
|-----------|----------|---------|
| `[ChannelPost]` | 300 | channel posts |
| `[MessageDice("🎰"/"🎲"/"🎯"/"🎳"/"🏀")]` | 250 | Telegram dice emojis |
| `[CallbackPrefix("…")]` | 200 | callback queries matched by prefix |
| `[Command("/…")]` | 100 + prefix length | text commands (longer prefix wins) |
| `[CallbackFallback]` | 1 | catch-all for unmatched callbacks |

Adding a new command = add a class implementing `IUpdateHandler`, decorate with a route attribute, register via `services.AddHandler<T>()` inside the owning module. No Host edit.

## Cross-module events

`IDomainEventBus.PublishAsync(IDomainEvent)` publishes synchronously to every subscriber whose pattern matches the event's `EventType`. Grammar:

- exact — `"sh.game_ended"`
- module wildcard — `"sh.*"`
- action wildcard — `"*.game_ended"`
- total wildcard — `"*"`

`EventSubscriptionInitializer` registers two framework subscribers on `"*"` before any module subscription runs:

- `EventLogSubscriber` → appends to `event_log` (Postgres) — powers the `/admin/events` and `/admin/history` pages.
- `ClickHouseEventMirror` → forwards to ClickHouse `events_v2` — powers Grafana dashboards.

A module subscribing to another module's event (e.g. Poker listening for `"sh.game_ended"` to award a bonus) does **not** import the publishing module. Subscribers receive the event as JSON and pull only the fields they care about. Shared vocabulary = the event-type prefix.

## Event sourcing

Two styles coexist:

- **Classical** — `IRepository<TAggregate>` with a row-per-aggregate store (Dapper via `EfRepository` — the name is legacy, there is no EF). Used by most live games.
- **Event-sourced** — module registers an aggregate via `services.RegisterAggregate<T>(PersistenceStrategy.EventSourced)`. Aggregates emit `IDomainEvent`s appended to `PostgresEventStore` (`module_events` table) with optimistic concurrency. Aggregates can `ISnapshotable` to bound replay cost — `PostgresSnapshotStore<T>` persists every Nth event's state to `module_snapshots`. Used by Poker, SecretHitler, Blackjack.

Projections (`IProjection`) subscribe to events and run **in the same transaction as the event append**, so read models and event log can't diverge.

`event_log` is separate from `module_events`: `module_events` only stores ES-aggregate events (replayable state), while `event_log` is a flat audit trail of every `IDomainEvent` ever published (including from classical modules like Darts/DiceCube that don't event-source). The admin History page queries `event_log`.

## Migrations

`FrameworkMigrations` ships the framework-owned DDL (users, economics ledger, `__module_migrations`, `module_events`, `module_snapshots`, `event_log`). Each module implements `IModuleMigrations.GetMigrations()` returning an ordered list of `IMigration` with stable string ids. `ModuleMigrationRunner` applies the framework migrations first, then each module's, tracking state in `__module_migrations` keyed by `(module_id, migration_id)`. No EF tooling; migrations are raw SQL strings executed via Dapper.

Forward-only. Adding a migration = append to the module's `GetMigrations()` list.

## Analytics

`ClickHouseAnalyticsService` is a `Singleton` + `IHostedService`:

- On start: `CREATE TABLE IF NOT EXISTS analytics.events_v2 (...)` with `MergeTree` engine ordered by `(project, module, event_type, created_at)`.
- `Track(moduleId, eventName, tags)` enqueues a row; flushed every `FlushIntervalMs` via `ClickHouseBulkCopy` (parameterized, no string concat).
- `TrackDomainEvent(IDomainEvent)` reflects over event properties → `params Map(String, String)` + full JSON payload. Wired through `ClickHouseEventMirror` on the `"*"` subscription.
- `ExceptionMiddleware` emits `_framework.error` with exception type/stack — error dashboards live off that.

Disabled by setting `ClickHouse:Enabled=false`.

## Admin UI

Modules contribute admin pages by returning `IAdminPage` instances from `IModule.GetAdminPages()`. `AdminMount` gates every `/admin/*` request with a constant-time compare against `Bot:AdminWebToken` (`?token=…` query or `admin_token` cookie). If the token is unset, `/admin/*` returns 503.

The Host ships shared admin pages (`/admin`, `/admin/events`, `/admin/history`, `/admin/bets`) that query framework-owned tables, so a distribution gets an observability surface for free without a game having to implement one.

## Per-module options

Modules bind their own config subtree:

```csharp
public void ConfigureServices(IModuleServiceCollection services)
{
    services.BindOptions<PokerOptions>("Games:poker");
    // ...
}
```

Convention: `Games:<moduleId>` for game settings, `Bot` for framework-wide settings, `ClickHouse` for analytics, `ConnectionStrings:Postgres` for the DB. No `BotOptions` god-object — each module owns its options class.

## What's actually wired vs. contracts-only

| Capability | SDK contract | Host implementation | Used by games? |
|------------|--------------|---------------------|----------------|
| Attribute routing | ✓ | ✓ | all games |
| Economics ledger | ✓ | ✓ | all games |
| Analytics | ✓ | ✓ (ClickHouse) | all games |
| Localizer | ✓ | ✓ | all games |
| Domain event bus | ✓ | ✓ (in-process) | cross-module (SH→Poker, etc.) |
| Event store + snapshots | ✓ | ✓ (Postgres) | Poker, SecretHitler, Blackjack |
| Classical repository | ✓ | ✓ (Dapper) | most games |
| Module migrations | ✓ | ✓ | all games |
| Admin pages | ✓ | ✓ | SecretHitler, Admin module, Horse |
| Background jobs | ✓ | ✓ | Poker (turn timeouts), Blackjack (hand timeouts) |
| Health checks | ✓ | ✓ (`/health`) | framework-only so far |
| Command bus + CQRS | ✓ | ✓ | not yet adopted — games still dispatch inline |
| Metrics (`IMetrics`) | ✓ | stub | — |
| Feature flags (`IFeatureFlags`) | ✓ | stub | — |

The "stub" rows are there as stable contracts so games can start using them today; the Host side will grow a real Prometheus / GrowthBook implementation without the modules changing.

## Key tradeoffs

1. **Modules own their schema.** Each module ships an `IModuleMigrations`. Framework-owned tables are limited to users, economics ledger, `module_events`, `module_snapshots`, `event_log`, and `__module_migrations` itself.
2. **Locales are a module responsibility.** Each module exposes `LocaleBundle` instances keyed by culture. Russian ships today.
3. **Options are per-module.** No shared god-object; every section binds via `BindOptions<T>`.
4. **Routing is attribute-based across every loaded module.** Zero Host change to add a command.
5. **Event sourcing is opt-in per aggregate.** Classical and ES styles share the same application-service shape.
6. **Projections + event-log append run in the same transaction as the event-store append.** Read models and audit never diverge from the stream.
7. **Stateless games need no aggregate ceremony.** Dice/Darts/DiceCube/Basketball/Bowling do per-message debit/credit + event publish, no `IRepository` involved.
8. **Admin pages are module contributions.** No central Pages tree to edit when a new game ships.
9. **Background jobs are module-local.** `IBackgroundJob` hides the `Microsoft.Extensions.Hosting` dependency; `BackgroundJobRunner` handles lifecycle + restart.
10. **Cross-module events are pattern-matched JSON, not typed imports.** No module ever imports another module's types.
11.  `EventLogSubscriber` and `ClickHouseEventMirror` subscribe on `"*"` before any module runs, so every new event flows into admin history + Grafana with zero wiring.
12. `BotFramework.Sdk.Testing` ships in-memory stubs so modules test their domain and application services with no external infra.

