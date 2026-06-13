# CasinoShiz Architecture

This document is a diagram-first view of the current CasinoShiz architecture.
For feature details and configuration keys, see [docs.md](docs.md). For operational
procedures, see [operations.md](operations.md).

## System Context

```mermaid
flowchart LR
    player["Telegram users<br/>private chats and groups"]
    admin["Bot administrators"]
    telegram["Telegram Bot API"]

    subgraph system["CasinoShiz"]
        host["ASP.NET Core host<br/>Telegram bot + admin UI"]
        modules["Game modules"]
        webapp["PixelBattle WebApp"]
    end

    postgres[("PostgreSQL<br/>wallets, games, events")]
    redis[("Redis<br/>update streams + CAP transport")]
    clickhouse[("ClickHouse<br/>product analytics")]
    monitoring["Prometheus + Grafana"]

    player <-->|"commands, callbacks, dice"| telegram
    telegram <-->|"polling or webhook"| host
    player <-->|"Telegram WebApp"| webapp
    admin -->|"HTTPS /admin"| host

    host --> modules
    host --> webapp
    host <--> postgres
    host <--> redis
    host --> clickhouse
    monitoring -->|"scrape and query"| host
    monitoring --> redis
    monitoring --> postgres
    monitoring --> clickhouse
```

The ASP.NET Core process is the composition root and runtime boundary. Game modules
do not host separate processes; they register handlers, services, migrations,
locales, projections, event subscribers, jobs, and Telegram commands into the host.

## Runtime Containers

```mermaid
flowchart TB
    subgraph aspnet["CasinoShiz.Host process"]
        http["ASP.NET endpoints<br/>webhook, health, admin, PixelBattle"]
        driver["BotHostedService<br/>polling and command registration"]
        pipeline["UpdatePipeline"]
        router["UpdateRouter"]
        framework["Shared framework services<br/>economics, tuning, events, analytics"]
        games["Game and utility modules"]
        jobs["Hosted jobs<br/>migrations, catch-up, sweepers"]
    end

    pg[("PostgreSQL")]
    rd[("Redis")]
    ch[("ClickHouse")]
    tg["Telegram Bot API"]

    tg --> driver
    tg --> http
    driver --> pipeline
    http --> pipeline
    pipeline --> router
    router --> games
    games --> framework
    jobs --> games
    jobs --> framework

    framework <--> pg
    games <--> pg
    framework <--> rd
    framework --> ch
    games --> tg
```

## Host Composition

`Program.cs` selects the distribution by listing modules. Each module owns its
internal DI registration and contributes metadata to the shared host.

```mermaid
flowchart LR
    program["Program.cs"]
    builder["AddBotFramework()"]
    addmodule["AddModule&lt;T&gt;()"]

    subgraph framework["Framework registration"]
        telegram["Telegram client"]
        update["Update pipeline + router"]
        economics["Economics + daily limits"]
        tuning["Runtime tuning"]
        events["Event store + event bus"]
        analytics["Analytics"]
        admin["Admin auth + health"]
    end

    subgraph contribution["Module contribution"]
        handlers["IUpdateHandler"]
        services["Module services"]
        commands["BotCommand"]
        locales["LocaleBundle"]
        migrations["IModuleMigrations"]
        projections["Projections/subscribers"]
        modulejobs["Background jobs"]
    end

    loaded["LoadedModules aggregate"]
    app["WebApplication"]

    program --> builder
    program --> addmodule
    builder --> framework
    addmodule --> contribution
    contribution --> loaded
    framework --> app
    loaded --> app
```

Current module families:

- Telegram dice: slots, dice cube, darts, football, basketball, bowling;
- stateful games: blackjack, poker, Secret Hitler, horse racing;
- social and utility: challenges, pick/lotteries, transfer, redeem, PixelBattle;
- shared views: leaderboard, seasonal meta, admin and debug.

## Telegram Update Flow

Polling and webhook delivery use the same processing pipeline. Redis changes the
transport between ingestion and processing, not the handler model.

```mermaid
flowchart TD
    update["Telegram Update"]
    mode{"Ingress"}
    polling["BotHostedService<br/>GetUpdates"]
    webhook["POST /{bot-token}"]
    redisEnabled{"Redis enabled?"}
    publisher["UpdateStreamPublisher"]
    streams[("Redis streams<br/>partition = abs(chatId) mod N")]
    worker["UpdateStreamWorkerService"]
    scope["Create DI scope"]
    pipeline["UpdatePipeline"]
    router["UpdateRouter"]
    handler["First matching module handler"]

    update --> mode
    mode --> polling
    mode --> webhook
    polling --> redisEnabled
    webhook --> redisEnabled
    redisEnabled -->|"yes"| publisher
    publisher --> streams
    streams --> worker
    worker --> scope
    redisEnabled -->|"no"| scope
    scope --> pipeline
    pipeline --> router
    router --> handler
```

### Pipeline And Routing

```mermaid
sequenceDiagram
    participant Source as Polling/Webhook/Redis worker
    participant Scope as Request DI scope
    participant Error as Exception middleware
    participant Dedup as Deduplication middleware
    participant Log as Logging middleware
    participant Rate as Rate-limit middleware
    participant Known as Known-chats middleware
    participant Router as UpdateRouter
    participant Handler as Module handler
    participant Service as Game service

    Source->>Scope: create scope and UpdateContext
    Scope->>Error: InvokeAsync
    Error->>Dedup: next
    Dedup->>Log: next
    Log->>Rate: next
    Rate->>Known: next
    Known->>Router: dispatch update
    Router->>Router: scan priority-sorted routes
    Router->>Handler: resolve first match from DI
    Handler->>Service: execute command/callback
    Service-->>Handler: result
    Handler-->>Source: Telegram response completed
```

The effective middleware list is assembled by the framework registrations.
Routing attributes have descending priority:

```mermaid
flowchart LR
    channel["ChannelPost<br/>300"]
    dice["MessageDice<br/>250"]
    callback["CallbackPrefix<br/>200"]
    command["Command<br/>100 + length"]
    text["TextCommand<br/>90 + length"]
    fallback["CallbackFallback<br/>1"]

    channel --> dice --> callback --> command --> text --> fallback
```

Only the first matching route is dispatched. Longer command names win ties, so
`/picklottery` takes precedence over `/pick`.

## Redis Update Delivery

Redis Streams preserve per-chat ordering by assigning the same chat to the same
partition. Different partitions can execute concurrently.

```mermaid
flowchart LR
    publisher["UpdateStreamPublisher"]
    p0[("updates:0")]
    p1[("updates:1")]
    pn[("updates:N-1")]

    w0["partition:0 consumer"]
    w1["partition:1 consumer"]
    wn["partition:N-1 consumer"]
    pipeline["UpdatePipeline"]
    retry[("retry counter<br/>with TTL")]
    dlq[("dead-letter stream")]

    publisher --> p0
    publisher --> p1
    publisher --> pn
    p0 --> w0
    p1 --> w1
    pn --> wn
    w0 --> pipeline
    w1 --> pipeline
    wn --> pipeline
    pipeline -->|"failure"| retry
    retry -->|"attempts below limit"| w0
    retry -->|"attempts exhausted"| dlq
```

An entry is acknowledged only after successful pipeline processing. Failed entries
remain pending and are retried. After `MaxProcessingAttempts`, the payload and error
metadata are moved to the dead-letter stream and the original entry is acknowledged.

## Module Request Pattern

Most command paths follow the same dependency direction:

```mermaid
flowchart LR
    route["Route attribute"]
    handler["Presentation handler"]
    service["Application service"]
    domain["Domain model / rules"]
    store["Store or repository"]
    shared["Shared host services"]
    pg[("PostgreSQL")]
    telegram["Telegram Bot API"]

    route --> handler
    handler --> service
    service --> domain
    service --> store
    service --> shared
    store --> pg
    shared --> pg
    handler --> telegram
```

Handlers parse Telegram input and render responses. Services own use-case
orchestration. Domain objects own game rules where a game has a DDD split.
Stores and repositories own persistence. Cross-cutting balance, analytics, tuning,
locking, and event behavior belongs to framework services.

## Wallet And Ledger

Wallet identity is `(telegram_user_id, balance_scope_id)`. The balance scope is
normally the Telegram chat id, so one person has independent balances in different
groups and in private chat.

```mermaid
sequenceDiagram
    participant Game as Game service
    participant Econ as EconomicsService
    participant PG as PostgreSQL

    Game->>Econ: TryDebitOnce(user, chat, amount, operationId)
    Econ->>PG: BEGIN
    Econ->>PG: SELECT wallet FOR UPDATE
    Econ->>PG: check operation_id and balance
    alt valid and not previously applied
        Econ->>PG: UPDATE users balance/version
        Econ->>PG: INSERT economics_ledger
        Econ->>PG: COMMIT
        Econ-->>Game: applied + new balance
    else duplicate operation
        Econ->>PG: ROLLBACK
        Econ-->>Game: already applied/rejected
    else insufficient funds
        Econ->>PG: ROLLBACK
        Econ-->>Game: rejected
    end
```

Important guarantees:

- `SELECT ... FOR UPDATE` serializes concurrent mutations to one wallet;
- wallet update and ledger append happen in one transaction;
- operation ids make critical debits, credits, transfers, refunds, and prizes idempotent;
- the ledger is append-only; admin recovery writes compensating rows.

## Event-Sourced Aggregate Flow

Event-sourced modules persist aggregate events first. Dispatch happens after append
commit, then updates read models, cross-module subscribers, and analytics.

```mermaid
sequenceDiagram
    participant Handler
    participant Repo as EventSourcedRepository
    participant Aggregate
    participant Store as PostgresEventStore
    participant Dispatcher as EventDispatcher
    participant Projection
    participant Bus as Domain event bus
    participant Subscriber
    participant Analytics

    Handler->>Repo: load + execute command
    Repo->>Store: read stream/snapshot
    Store-->>Repo: history
    Repo->>Aggregate: rehydrate and mutate
    Aggregate-->>Repo: uncommitted events
    Repo->>Store: append with expected version
    Store-->>Repo: committed versions
    Repo->>Dispatcher: dispatch each committed event
    Dispatcher->>Projection: apply read-model update
    Dispatcher->>Bus: publish cross-module event
    Bus->>Subscriber: matching pattern
    Dispatcher->>Analytics: track event metadata
```

Dispatch is post-commit. A projection/subscriber failure does not roll back the event
append. Failures are persisted for retry, and event replay can rebuild projections.

## Domain Event Bus Modes

```mermaid
flowchart TD
    dispatcher["EventDispatcher"]
    enabled{"Redis enabled?"}

    inproc["InProcessEventBus<br/>sequential, same process"]
    cap["CapEventBus"]
    outbox[("PostgreSQL CAP outbox")]
    transport[("Redis CAP transport")]
    consumer["CapEventConsumer"]
    subscribers["Pattern-matched subscribers"]

    dispatcher --> enabled
    enabled -->|"no"| inproc
    inproc --> subscribers
    enabled -->|"yes"| cap
    cap --> outbox
    outbox --> transport
    transport --> consumer
    consumer --> subscribers
```

Subscriptions use event-name patterns such as `sh.game_ended`, `sh.*`,
`*.game_ended`, or `*`. Subscribers must be idempotent because distributed
delivery is at least once.

## Seasonal Meta Projections

Game modules publish `meta.game_completed`. The Meta module projects the event into
several independent features.

```mermaid
flowchart LR
    completed["GameCompletedMetaEvent"]

    xp["MetaXpProjection"]
    quests["QuestProjection"]
    clans["ClanProjection"]

    profile[("season player<br/>XP, level, rating, totals")]
    achievements[("player achievements")]
    risk[("risk flags")]
    questProgress[("daily/weekly quest progress")]
    clanProgress[("season clan XP/rating")]

    completed --> xp
    completed --> quests
    completed --> clans

    xp --> profile
    xp --> achievements
    xp --> risk
    quests --> questProgress
    clans --> clanProgress
```

`/menu`, `/profile`, `/quests`, `/achievements`, `/clan`, and `/topseason`
read these projections. Tournament brackets are part of the same module but use
their own command-driven tables and idempotent economics operations.

## Runtime Tuning

Runtime settings are a database overlay on top of file and environment
configuration.

```mermaid
flowchart LR
    file["appsettings.json"]
    env["Environment variables"]
    admin["Admin structured forms<br/>or JSON patch"]
    sanitizer["RuntimeTuningPayloadSanitizer"]
    validation["Typed merge validation"]
    table[("runtime_tuning.payload")]
    accessor["RuntimeTuningAccessor"]
    effective["Effective typed options"]
    games["Game/framework services"]

    file --> accessor
    env --> accessor
    admin --> sanitizer
    sanitizer --> validation
    validation --> table
    table --> accessor
    accessor --> effective
    effective --> games
```

Precedence is:

1. `appsettings.json`;
2. environment variables;
3. whitelisted database patch.

Saving from `/admin/settings` sanitizes keys, validates typed sections, writes the
JSONB patch, reloads the accessor, and appends an admin audit record. Services that
read `IRuntimeTuningAccessor` receive changes without process restart.

## Admin And HTTP Surface

```mermaid
flowchart TB
    request["HTTP request"]
    path{"Path"}

    health["/health/live<br/>/health/ready"]
    webhook["/{bot-token}<br/>production webhook"]
    pixel["/pixelbattle/*<br/>WebApp + API + SSE"]
    adminGate["/admin session gate"]
    login["login/auth/logout"]
    pages["Razor admin pages"]

    token["Token form or<br/>Telegram Login Widget"]
    session["AdminSession<br/>SuperAdmin / ReadOnly"]
    audit[("admin_audit")]

    request --> path
    path --> health
    path --> webhook
    path --> pixel
    path --> adminGate
    adminGate --> login
    login --> token
    token --> session
    adminGate -->|"authenticated"| pages
    pages -->|"write actions"| audit
```

Read-only admins can inspect operational pages but mutation handlers return `403`.
SuperAdmin writes include balance changes, race actions, runtime tuning, and other
administrative operations.

## Deployment Topology

### Docker Compose

```mermaid
flowchart TB
    internet["Telegram / browser"]

    subgraph compose["Docker Compose network"]
        bot["CasinoShiz bot"]
        postgres[("PostgreSQL 16")]
        redis[("Redis")]
        clickhouse[("ClickHouse")]
        prometheus["Prometheus"]
        grafana["Grafana"]
        pgexporter["postgres-exporter"]
        redisexporter["redis-exporter"]
        cadvisor["cAdvisor"]
        monitor["dotnet-monitor"]
    end

    internet --> bot
    bot <--> postgres
    bot <--> redis
    bot --> clickhouse

    pgexporter --> postgres
    redisexporter --> redis
    monitor --> bot
    prometheus --> pgexporter
    prometheus --> redisexporter
    prometheus --> cadvisor
    prometheus --> monitor
    grafana --> prometheus
    grafana --> clickhouse
```

### Kubernetes / Helm

```mermaid
flowchart TB
    telegram["Telegram"]
    ingress["Ingress"]

    subgraph cluster["Kubernetes cluster"]
        deployment["CasinoShiz Deployment"]
        service["Bot Service"]
        pgsvc["Postgres Service"]
        pgstate[("Postgres StatefulSet")]
        rdsvc["Redis Service"]
        rdstate[("Redis StatefulSet")]
        secret["Kubernetes Secret"]
    end

    telegram --> ingress
    ingress --> service
    service --> deployment
    secret --> deployment
    deployment --> pgsvc
    pgsvc --> pgstate
    deployment --> rdsvc
    rdsvc --> rdstate
```

The shipped Helm chart includes the bot, PostgreSQL, and Redis. ClickHouse and the
monitoring stack are external or disabled by default in that topology.

## Failure And Recovery Boundaries

```mermaid
flowchart LR
    updateFailure["Update processing failure"]
    eventFailure["Projection/event dispatch failure"]
    infraFailure["Optional analytics failure"]

    updateRetry["Redis pending retry<br/>or polling retry"]
    updateDlq["Update DLQ"]
    failureStore[("event_dispatch_failures")]
    retry["Admin/debug retry"]
    replay["Event replay / projection rebuild"]
    graceful["Log and continue<br/>core gameplay remains available"]

    updateFailure --> updateRetry
    updateRetry -->|"attempts exhausted"| updateDlq
    eventFailure --> failureStore
    failureStore --> retry
    failureStore --> replay
    infraFailure --> graceful
```

PostgreSQL is the primary consistency boundary. Redis improves coordination and
distributed delivery. ClickHouse and dashboards are operationally useful but do not
own game or wallet state.

## Dependency Rules

```mermaid
flowchart BT
    host["CasinoShiz.Host"]
    game["Games.* modules"]
    frameworkHost["BotFramework.Host"]
    sdk["BotFramework.Sdk"]

    host --> game
    host --> frameworkHost
    game --> frameworkHost
    game --> sdk
    frameworkHost --> sdk
```

- `BotFramework.Sdk` contains contracts and shared event vocabulary.
- `BotFramework.Host` implements infrastructure and cross-cutting services.
- each `Games.*` project owns one bounded feature/module;
- `CasinoShiz.Host` selects modules and maps distribution-specific endpoints;
- modules should communicate through SDK contracts and domain events rather than
  importing another game's internals.
