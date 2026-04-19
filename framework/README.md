# Bot framework sketch

**Status: design sketch, not wired into `CasinoShiz.slnx`, not expected to build.**

Explores what CasinoShiz could look like refactored into a reusable Telegram bot framework:

- `IModule` as the registration contract — a game ships as an assembly implementing one interface.
- Repositories hide `DbContext` from application services so a module can persist classical state *or* event-sourced events without callers caring.
- Event sourcing is **opt-in per aggregate** — the default is classical state persistence; complex turn-based aggregates (poker hands, Secret Hitler rooms) can upgrade where replay/debugging pays rent.

## Layering

```
┌────────────────────────────────────────────────────────────────────────────┐
│ L4  Presentation                                                           │
│     IUpdateHandler ([Command]/[CallbackPrefix])  IRenderer<T>  IAdminPage  │
│     Games.*Handler — thin adapter: parse Update → ICommand → render        │
├────────────────────────────────────────────────────────────────────────────┤
│ L3  Domain                                                                 │
│     IAggregateRoot / IEventSourcedAggregate / IDomainEvent                 │
│     PokerTable · SecretHitlerGame · Dice (stateless)                       │
│     ICommandHandler<TCommand> · IProjection · IDomainEventSubscriber       │
├────────────────────────────────────────────────────────────────────────────┤
│ L2  Platform                                                               │
│     ICommandBus + ICommandMiddleware  (logging / metrics / rate-limit)     │
│     IDomainEventBus (in-process, pattern-matched)                          │
│     IRepository<T> · IEventStore · ISnapshotStore<T>                       │
│     IMetrics · IFeatureFlags · IHealthCheck · ILocalizer                   │
│     IEconomicsService · IAnalyticsService  (cross-module services)         │
├────────────────────────────────────────────────────────────────────────────┤
│ L1  Infrastructure                                                         │
│     Postgres · ClickHouse · Telegram Bot API · ASP.NET Core host           │
│     PostgresEventStore · PostgresSnapshotStore · NpgsqlConnectionFactory   │
└────────────────────────────────────────────────────────────────────────────┘
```

Modules only import L2 contracts. They never reach down to L1 (no `DbContext`,
no `HttpClient`, no `Microsoft.Extensions.Hosting`) and never reach across to
each other's L3 (Poker can subscribe to an SH event by pattern, but cannot
reference an SH type). That invariant is what makes a module portable between
Host distributions.

## Layout

```
experiments/bot-framework-sketch/
├── sdk/                                  framework contracts (SDK assembly)
│   ├── IModule.cs                             module lifecycle + registration surface
│   ├── Aggregates.cs                          IAggregateRoot / IEventSourcedAggregate / IDomainEvent / IRepository / IEventStore
│   ├── Snapshots.cs                           ISnapshotable / ISnapshotStore / SnapshotPolicy — bounded replay cost for ES
│   ├── Projections.cs                         IProjection / IRebuildableProjection — read models over event streams
│   ├── ModuleMigrations.cs                    IModuleMigrations / Migration — per-module schema ownership
│   ├── AdminContribution.cs                   IAdminPage (Tier 1) / IRazorAdminModule (Tier 2) / AdminMenu
│   ├── Commands.cs                            ICommand / ICommandHandler / ICommandBus — CQRS dispatch surface
│   ├── Pipeline.cs                            ICommandMiddleware / CommandContext / RequestContext
│   ├── DomainEventBus.cs                      IDomainEventBus / IDomainEventSubscriber — cross-module events
│   ├── HealthChecks.cs                        IHealthCheck (Liveness / Readiness) + HealthStatus
│   ├── FeatureFlags.cs                        IFeatureFlags + FeatureFlagContext (per-user rollouts)
│   ├── Metrics.cs                             IMetrics — counter / histogram / gauge
│   └── Testing.cs                             InMemoryRepository<T> / InMemoryEventStoreRepository<T> / FakeEconomicsService
├── host/                                 infra the Host assembly supplies
│   ├── Program.cs                             composition root — reference modules, list them, run
│   ├── ModuleLoader.cs                        drives ConfigureServices; collects locales, commands, migrations
│   ├── ModuleMigrationRunner.cs               applies each module's migrations against __module_migrations tracking table
│   ├── UpdateRouter.cs                        attribute-based routing across every loaded module assembly
│   ├── HostServices.cs                        IEconomicsService / IAnalyticsService / ILocalizer / IRenderer
│   ├── EventDispatcher.cs                     fans events from the store into projections + subscribers + analytics, same transaction
│   ├── AdminMount.cs                          mounts Tier-1 admin pages under /admin/<moduleId>/<route>
│   ├── PostgresEventStore.cs                  concrete IEventStore with module_events DDL + optimistic concurrency
│   ├── PostgresSnapshotStore.cs               concrete ISnapshotStore<TAggregate> with module_snapshots DDL
│   ├── CommandBus.cs                          reflective handler dispatch + cached MethodInfo + AsyncLocal RequestContext
│   ├── InProcessEventBus.cs                   pattern-matched subscription dispatch ("sh.game_ended", "sh.*", "*.game_ended", "*")
│   ├── HealthEndpoint.cs                      aggregates IHealthCheck into /health + /health/live + /health/ready
│   └── Middleware/
│       ├── LoggingMiddleware.cs                   outermost — structured log line per command w/ trace id
│       ├── MetricsMiddleware.cs                   bot_commands_total counter + bot_command_duration_ms histogram
│       └── RateLimitMiddleware.cs                 per (userId, commandType) token bucket; throws RateLimitedException
└── games/
    ├── Dice/                             stateless game example
    │   ├── DiceModule.cs                      per-message debit/credit, no aggregate, no repository
    │   └── Migrations.cs                      001_initial: dice_rolls audit table
    ├── Poker/                            classical state-based aggregate + EF repository
    │   ├── PokerModule.cs                     module registration + PokerService + PokerHandler ([Command("/poker")])
    │   ├── PokerTable.cs
    │   ├── EfPokerRepository.cs
    │   ├── PokerTurnTimeoutJob.cs             IBackgroundJob example — module-owned sweeper
    │   ├── PokerServiceTests.cs               test example using InMemoryRepository<PokerTable>
    │   ├── Migrations.cs                      001_initial: poker_tables + poker_seats
    │   └── Subscriptions/
    │       └── ShGameEndedSubscriber.cs           cross-module: Poker credits bonus on SH win (JSON payload, no SH import)
    └── SecretHitler/                     event-sourced aggregate + snapshots + projections + admin
        ├── SecretHitlerModule.cs              implements IRazorAdminModule — opts into Tier-2 admin RCL
        ├── Events.cs
        ├── SecretHitlerGame.cs                implements ISnapshotable
        ├── EventStoreRepository.cs            snapshot-aware — restore + replay-since-snapshot
        ├── ShActiveRoomsProjection.cs         IProjection example
        ├── ShAdminPage.cs                     IAdminPage example (Tier 1)
        ├── Migrations.cs                      001_projections: sh_active_rooms
        └── SecretHitler.Admin/                Razor Class Library — Tier-2 admin
            ├── SecretHitler.Admin.csproj
            └── Areas/Sh/Pages/
                ├── _ViewImports.cshtml
                ├── Rooms.cshtml
                └── Rooms.cshtml.cs
```

## Key tradeoffs encoded here

1. **Modules own their schema.** Each module ships an `IModuleMigrations` — an ordered list of SQL migrations with stable IDs. The Host's `ModuleMigrationRunner` applies them per-module against `__module_migrations` (namespaced by `moduleId`), verifies content hashes to catch retroactive edits, and only then starts the app. Shared tables are limited to `module_events`, `module_snapshots`, and the migration tracker itself — all Host-owned. Modules access storage directly via Dapper; no shared `DbContext`, no cross-module EF coupling.

2. **Locales are a module responsibility.** Each module exposes its own resource dictionary (`ru`, `en`, …). The Host supplies the current-culture resolver but doesn't own any game strings. `Helpers/Locales.cs` in the current codebase is the anti-pattern this fixes.

3. **Options are per-module.** `IModule.ConfigureServices` binds `Games:poker`, `Games:sh`, etc. to typed option classes. No more `BotOptions` god-object.

4. **Routing stays attribute-based.** The Host's `UpdateRouter` scans *all* loaded module assemblies, not just `CasinoShiz.Core`. One line changes in the Host; modules change nothing.

5. **Event sourcing is a choice, not a mandate.** `IRepository<TAggregate>` is the uniform contract; its implementation can be EF-backed (classical) or event-store-backed (ES). Same application service code either way.

6. **Projections run in the same transaction as the event append.** A read-model upsert committing atomically with the event that caused it means admin pages and analytics never show "event exists but the derived view hasn't caught up". Simpler than async projectors with lag monitoring; graduates to async only for projections that outgrow it.

7. **Stateless games fit without aggregate ceremony.** Dice/slots/coinflip modules implement `IModule` just like Poker — they simply don't call `RegisterAggregate`. They own their audit-history tables through `ConfigureEntities`, debit/credit the shared economics ledger, and emit analytics. No aggregate root forced on them.

8. **Admin pages are module contributions.** A module ships `IAdminPage` instances; the Host mounts them under `/admin/<moduleId>/*` behind the same `AdminWebToken` middleware the rest of the admin UI uses. No central Pages tree to edit when a new game ships.

9. **Background jobs are module-local.** A module registers `IBackgroundJob` implementations; the Host wraps them as `IHostedService` with crash-and-restart policy. Module never imports `Microsoft.Extensions.Hosting`.

10. **Testing is a first-class story.** `BotFramework.Sdk.Testing` ships in-memory stubs for every host contract — a module author writes xUnit tests against their application services with zero external infrastructure.

11. **Snapshotting is per-aggregate, per-module cadence.** Aggregates implement `ISnapshotable`; the Host reads the cadence from `SnapshotPolicy` (`Every = N` events) and persists to `module_snapshots`. On load, replay walks only events newer than the snapshot. Aggregates that don't opt in fall through to full replay — no cost paid, no ceremony needed.

12. **Admin UI has two tiers.** Tier 1 is `IAdminPage` — pure data contribution, no ASP.NET dep in the module. Tier 2 is a companion Razor Class Library; the module implements `IRazorAdminModule` and the Host adds the RCL's pages to its Razor application parts. Modules pick per-page: lists/counters use Tier 1, form-heavy admin uses Tier 2.

13. **Cross-cutting concerns live in a command pipeline, not handlers.** Handlers parse an `Update`, build an `ICommand`, and dispatch through `ICommandBus`. The bus runs every registered `ICommandMiddleware` in order — logging, metrics, rate-limiting, auth — before reaching the terminal `ICommandHandler<TCommand>`. Result: a new game ships without re-implementing log lines, latency histograms, or per-user throttling, and a new cross-cutting concern (e.g., idempotency) gets added once at the Host layer. The pipeline is composed once at startup; runtime dispatch is O(1) via cached reflection.

14. **Cross-module events are pattern-matched JSON, not typed imports.** `IDomainEventBus.Subscribe(pattern, subscriber)` takes grammar like `"sh.game_ended"`, `"sh.*"`, `"*.game_ended"`, or `"*"`. Subscribers receive the event as JSON and pull the fields they care about. Poker subscribing to an SH event does NOT `using Games.SecretHitler;` — the only shared vocabulary is the type-name prefix. Dispatch runs in the same transaction as the event-store append so an external-effect failure rolls back the event (in-process semantics); swapping to Kafka would move to at-least-once and require outbox handling.

15. **Operational concerns are first-class SDK contracts.** `IHealthCheck`, `IMetrics`, and `IFeatureFlags` live in the SDK alongside `IRepository<T>`. A module emits `bot_commands_total` without importing Prometheus, gates a risky path behind `flags.IsEnabled("sh.experimental-veto-rule", ctx)` without importing a flag SDK, and contributes a readiness probe without importing `Microsoft.Extensions.Diagnostics.HealthChecks`. The Host picks backends (Prometheus scrape, GrowthBook, native .NET health) at composition time. Modules stay deployable against any of them.

## What this sketch deliberately omits

- Actual DI container wiring — `Program.cs` uses stub adapters where concrete MS.DI types would plug in.
- csproj / assembly boundaries for the non-RCL projects. Every `.cs` file here is pseudo-C# for the design story; a real cutover splits them into `BotFramework.Sdk`, `BotFramework.Host`, `Games.Poker`, etc. The one exception is `SecretHitler.Admin/SecretHitler.Admin.csproj`, which is sketched so the RCL structure is concrete.
- Migration rollback. Forward-only migrations match how the real codebase already behaves (EF `Down` methods are never invoked in prod). If rollback ever becomes a real need, add a `DownSql` field to `Migration` and a Host-side rewind flag.
- Cross-module transactions. No module can wrap another module's write in its own transaction — every module communicates via the Host abstractions (`IEconomicsService`, etc.). Consciously out of scope; cross-module atomicity would demand an orchestrator and that's not a game-platform problem yet.

## How to use this as a plan

Reading order:

1. `sdk/IModule.cs` — the whole contract a game author sees (aggregates, handlers, projections, admin pages, jobs, migrations, commands, events, health).
2. `sdk/Aggregates.cs` + `sdk/Snapshots.cs` + `sdk/Projections.cs` + `sdk/ModuleMigrations.cs` + `sdk/AdminContribution.cs` — persistence, read-model, schema, and UI contribution shapes.
3. `sdk/Commands.cs` + `sdk/Pipeline.cs` + `sdk/DomainEventBus.cs` — the L2 platform contracts modules dispatch through.
4. `sdk/HealthChecks.cs` + `sdk/FeatureFlags.cs` + `sdk/Metrics.cs` — operational contracts every module consumes.
5. `games/Dice/DiceModule.cs` + `games/Dice/Migrations.cs` — minimal stateless module; easiest starting point.
6. `games/Poker/*` — classical aggregate + background job + test example + schema + cross-module subscription.
7. `games/SecretHitler/*` — event-sourced aggregate with snapshots, projection, Tier-1 admin page, and `SecretHitler.Admin/` Tier-2 RCL.
8. `host/Program.cs` — how a bot distribution composes a specific set of modules, wires the pipeline, mounts /health.
9. `host/CommandBus.cs` + `host/InProcessEventBus.cs` + `host/Middleware/*` — the pipeline and cross-module bus implementations.
10. `host/UpdateRouter.cs` + `host/EventDispatcher.cs` + `host/PostgresEventStore.cs` + `host/PostgresSnapshotStore.cs` + `host/ModuleMigrationRunner.cs` + `host/AdminMount.cs` + `host/HealthEndpoint.cs` — load-bearing Host internals modules depend on but never import.

The two persistence styles share the same application-service shape. That's the point.

## Phased migration sketch (for when the framework is ready)

Applying this to the real CasinoShiz would look like:

- **Phase 1 — Per-module options.** Split `BotOptions` into `PokerOptions`, `DiceOptions`, `HorseOptions`, etc. Keep everything else. Mechanical, low-risk.
- **Phase 2 — `IModule` interface without reflection discovery.** Each game registers itself by an extension method the Host calls by name. Proves the contract without the assembly-scanning machinery.
- **Phase 3 — Repository abstraction for Poker + SH.** Introduce `IRepository<T>` for the two aggregate-shaped games. Services stop touching `AppDbContext` directly.
- **Phase 4 — Event store + projections for SH.** The highest-value ES slice; the rest of the bot stays classical.
- **Phase 5 — Admin contribution + locale split.** Pull each game's strings out of `Helpers/Locales.cs`, mount admin pages through the module.
- **Phase 6 — Host/SDK assembly split.** The point where `CasinoShiz.Core` becomes `BotFramework.Host`, and each game becomes its own project. Last because it's the riskiest refactor and benefits most from prior phases.

Every phase leaves the bot running. None of them require touching the real codebase until this sketch is signed off.
