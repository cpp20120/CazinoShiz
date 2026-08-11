# CasinoShiz Architecture

This document is a diagram-first view of the current CasinoShiz architecture.
For feature details and configuration keys, see [docs.md](docs.md). For operational
procedures, see [operations.md](operations.md).

This file is the source of truth for execution consistency and service boundaries.
It defines when a command uses a local Atomic transaction, when it uses the
state-only executor, and how a distributed Wagering saga coordinates Ledger and
Game Services. Other architecture notes may explain the rationale or a design
detail, but must not contradict the flows and ownership rules defined here.

## Repository Boundaries

```text
framework/
  BotFramework.Contracts/   transport-neutral messaging contracts
  BotFramework.Sdk/         module and domain abstractions
  BotFramework.Host/        backend infrastructure and composition
  BotFramework.Rendering/   bounded render queues + media artifact/history ports
  BotFramework.Telegram/    Telegram ingress, routing and delivery
  BotFramework.Discord/     Discord gateway, interactions and delivery
games/
  Games.X.Contracts/        logical interfaces and portable DTOs
  Games.X/                  backend application/domain/infrastructure
  Games.X.Telegram/         Telegram presentation adapter
  Games.X.Discord/          Discord presentation adapter
  Games.X.Transport.Grpc/   protobuf and remote adapters
host/
  CasinoShiz.Host/          combined compatibility deployment + Razor admin
  CasinoShiz.Backend/       Telegram-free backend process
  CasinoShiz.TelegramBff/   Telegram client process
  CasinoShiz.DiscordBff/    Discord client process
  CasinoShiz.AdminBff/      browser admin BFF without database access
tests/CasinoShiz.Tests/     behavior and dependency-boundary tests
```

Not every context requires every optional project. PixelBattle uses HTTP/SSE for
its WebApp, native-dice contexts share `Games.NativeDice.Transport.Grpc`, and
Horse rendering is isolated in `Games.Horse.Rendering`. See
[`games/README.md`](../games/README.md) for the ownership rules.

## System Context

```mermaid
flowchart LR
    player["Telegram and Discord users<br/>private chats, groups and servers"]
    admin["Bot administrators"]
    telegram["Telegram Bot API"]
    discord["Discord Gateway / API"]

    subgraph system["CasinoShiz"]
        telegramBff["Telegram BFF<br/>client adapters"]
        discordBff["Discord BFF<br/>client adapters"]
        backend["Backend<br/>game services + persistence"]
        legacy["Legacy Host<br/>combined deployment + admin UI"]
        contracts["Logical contracts<br/>interfaces + events"]
        webapp["PixelBattle WebApp"]
        identity["Identity service"]
        wallet["Wallet service"]
    end

    postgres[("PostgreSQL<br/>monolith: cazino")]
    backendpg[("PostgreSQL<br/>micro: backend")]
    identitypg[("PostgreSQL<br/>micro: identity")]
    walletpg[("PostgreSQL<br/>micro: wallet")]
    aggregator["Admin BFF<br/>composed read models"]
    redis[("Redis<br/>update streams + coordination")]
    capTransport[("CAP integration transport<br/>Local / Redis Streams / Kafka-Redpanda")]
    clickhouse[("ClickHouse<br/>product analytics")]
    monitoring["Prometheus + Grafana"]

    player <-->|"commands, callbacks, dice"| telegram
    player <-->|"commands, interactions, components"| discord
    telegram <-->|"polling or webhook"| telegramBff
    telegram <-->|"legacy mode"| legacy
    discord <-->|"gateway/webhooks"| discordBff
    discord <-->|"legacy mode"| legacy
    player <-->|"Telegram WebApp"| webapp
    admin -->|"HTTPS /admin"| legacy
    admin --> aggregator

    telegramBff --> contracts
    discordBff --> contracts
    backend --> contracts
    legacy --> contracts
    telegramBff -->|"gRPC adapters"| backend
    discordBff -->|"gRPC adapters"| backend
    backend --> webapp
    legacy --> webapp
    backend <--> postgres
    legacy <--> postgres
    backend <--> backendpg
    aggregator -->|"gRPC read APIs"| backend
    aggregator -->|"gRPC read APIs"| identity
    aggregator -->|"gRPC read APIs"| wallet
    identity --> identitypg
    wallet --> walletpg
    telegramBff <--> redis
    discordBff <--> redis
    legacy <--> redis
    backend <--> capTransport
    legacy <--> capTransport
    backend --> clickhouse
    legacy --> clickhouse
    monitoring -->|"scrape and query"| backend
    monitoring -->|"scrape and query"| legacy
    monitoring --> redis
    monitoring --> capTransport
    monitoring --> postgres
    monitoring --> clickhouse
```

The repository supports one codebase across four deployment shapes: the combined
compatibility host, the legacy microservices profile, the distributed per-game
Compose profile, and Kubernetes/Helm. `CasinoShiz.Host` keeps the existing
monolith path; BFFs and Backend can be split without changing logical contracts.
A bounded context can therefore remain in-process, run as a dedicated game
service, or cross a gRPC boundary without changing its application-facing API.

## Runtime Containers

```mermaid
flowchart TB
    subgraph bffProcess["CasinoShiz.TelegramBff process"]
        webhook["webhook / polling"]
        pipeline["UpdatePipeline"]
        router["UpdateRouter"]
        adapters["Games.*.Telegram adapters"]
        clients["contract interfaces<br/>gRPC implementations"]
    end
    subgraph discordProcess["CasinoShiz.DiscordBff process"]
        gateway["Discord gateway / interactions"]
        discordRouter["message + interaction routers"]
        discordAdapters["Games.*.Discord adapters"]
        discordClients["named gRPC clients"]
    end
    subgraph backendProcess["CasinoShiz.Backend process"]
        grpc["gRPC endpoints"]
        services["game application services"]
        framework["economics, events, analytics"]
        jobs["migrations, projections, sweepers"]
        http["health + PixelBattle HTTP/SSE"]
    end

    pg[("PostgreSQL")]
    rd[("Redis<br/>update streams + coordination")]
    cap[("CAP integration transport<br/>Local / Redis Streams / Kafka-Redpanda")]
    ch[("ClickHouse")]
    tg["Telegram Bot API"]
    ds["Discord Gateway / API"]

    tg --> webhook
    webhook --> pipeline
    pipeline --> router
    router --> adapters
    adapters --> clients
    clients --> grpc
    ds --> gateway
    gateway --> discordRouter
    discordRouter --> discordAdapters
    discordAdapters --> discordClients
    discordClients --> grpc
    grpc --> services
    services --> framework
    jobs --> services
    jobs --> framework

    framework <--> pg
    services <--> pg
    framework <--> rd
    framework <--> cap
    framework --> ch
    adapters --> tg
    discordAdapters --> ds
```

The BFF processes contain channel-specific ingress, UX and presentation only.
They call Backend through logical contracts and named gRPC clients; they do not
open game, Identity or Wallet databases. Backend instances may be the combined
all-games process, a legacy service, or one module-selected game process.

## Host Composition

Each composition root selects backend modules, Telegram adapters, Discord adapters,
or any combination of them. Backend modules own persistence, migrations and jobs;
channel adapters own handlers, UX and client presentation. Transport registration
belongs only to Backend/BFF programs.

```mermaid
flowchart LR
    program["Program.cs"]
    builder["AddBotFramework()<br/>or AddBackendFramework()<br/>or AddTelegramBff()<br/>or AddDiscordBackend()"]
    addmodule["AddModule&lt;T&gt;()"]

    subgraph framework["Framework registration"]
        telegram["Telegram runtime<br/>(adapter composition only)"]
        discord["Discord runtime<br/>(adapter composition only)"]
        update["Update pipeline + router<br/>(adapter composition only)"]
        economics["Economics + daily limits"]
        tuning["Runtime tuning"]
        events["Event store + event bus"]
        analytics["Analytics"]
        admin["Admin auth + health"]
    end

    subgraph contribution["Module contribution"]
        handlers["IUpdateHandler<br/>(Telegram module)"]
        discordHandlers["Discord message/interaction handlers"]
        services["Module services"]
        commands["BotCommand"]
        locales["LocaleBundle"]
        migrations["IModuleMigrations<br/>(backend module)"]
        projections["Projections/subscribers"]
        modulejobs["Background jobs"]
    end

    loaded["LoadedModules aggregate"]
    app["WebApplication"]

    program --> builder
    program --> addmodule
    builder --> framework
    addmodule --> contribution
    discord --> app
    discordHandlers --> loaded
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

Inside `BotFramework.Telegram`, polling and webhook delivery use the same processing
pipeline. This runs in `CasinoShiz.TelegramBff` or in the combined legacy Host.
Redis changes ingestion delivery, not the handler or logical client contracts.

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
    participant Analytics as Update analytics middleware
    participant Log as Logging middleware
    participant Rate as Rate-limit middleware
    participant Known as Known-chats middleware
    participant Router as UpdateRouter
    participant Handler as Module handler
    participant Service as Game service

    Source->>Scope: create scope and UpdateContext
    Scope->>Error: InvokeAsync
    Error->>Dedup: next
    Dedup->>Analytics: next
    Analytics->>Log: next
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

## Discord Interaction Flow

Discord has a separate gateway and interaction pipeline, but it stops at the same
logical application boundary as Telegram. The adapter owns Discord-specific
parsing and presentation; game commands, decisions, effects and events remain
channel-neutral.

```mermaid
flowchart TD
    gateway["Discord Gateway"]
    hosted["DiscordHostedService"]
    router["message / interaction router"]
    ux["UX rate limit + cooldown"]
    handler["channel handler"]
    contract["logical game contract"]
    grpc["named gRPC client"]
    backend["selected Backend / game service"]
    response["Discord embed / component response"]

    gateway --> hosted --> router --> ux --> handler
    handler --> contract
    contract --> grpc --> backend
    backend --> handler --> response
```

The router handles slash commands, autocomplete, buttons, selects and modals.
Multi-field input such as a table code, bet or raise amount is collected with a
modal. `DiscordEmbeds` renders structured responses and `DiscordLocalization`
provides RU/EN text. Rate limiting and cooldown checks run before the handler,
while the Backend executor remains authoritative for idempotency and state
concurrency.

Component custom IDs use persistent component tokens. A token is validated before
the component or modal handler runs, so a button created before a restart is
rejected as stale rather than being applied to a new process state. Discord
outbound messages use a durable outbox and can be claimed by any healthy BFF
replica. Discord integration tests exercise these routes with mock interaction
objects, including autocomplete, modal submission, localization and stale-token
rejection; they do not require a live Discord gateway.

## Redis Update Delivery

Redis Streams preserve per-chat ordering by assigning the same chat to the same
partition. Different partitions can execute concurrently.

This section is intentionally Redis-specific: Telegram/Discord ingress update
streams currently use Redis Streams. It is separate from the CAP integration
transport, whose alternatives are shown in the Domain Event Bus, outbox and
deployment diagrams as Local, Redis Streams, or Kafka/Redpanda.

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
    route["Telegram route / Discord interaction"]
    handler["Application handler"]
    contract["Contract interface"]
    transport{"Composition"}
    grpc["gRPC client adapter"]
    service["Backend application service"]
    domain["Domain model / rules"]
    store["Store or repository"]
    shared["Shared host services"]
    pg[("PostgreSQL")]
    channel["Telegram Bot API / Discord API"]

    route --> handler
    handler --> contract
    contract --> transport
    transport -->|"legacy Host"| service
    transport -->|"split BFF"| grpc
    grpc --> service
    service --> domain
    service --> store
    service --> shared
    store --> pg
    shared --> pg
    handler --> channel
```

Handlers live in `Games.*.Telegram` or `Games.*.Discord`, parse channel input, and
render channel responses. They know a logical interface, not whether it is local
or remote. Backend services own orchestration; domain objects own rules; stores
own persistence. Protobuf and generated clients remain in transport projects;
channel SDKs remain in channel adapters. Cross-cutting balance, analytics, tuning,
locking, and event behavior belongs to framework services.

## Atomic Execution Kernel And Effect System

The module contract and registration  are documented in
[`framework/README.md`](../framework/README.md#atomic-game-execution-and-effects).

Game and economy commands use the same framework envelope but select one of two
local execution kernels. Deployment transport does not change command semantics:
monolith calls and BFF-to-gRPC calls arrive at the same logical executor boundary.
The Atomic path below is the kernel for commands that include economic effects.

```mermaid
flowchart LR
    request["Telegram / Discord / gRPC / HTTP request"]
    envelope["Framework envelope<br/>stable command id"]
    transaction["PostgreSQL transaction"]
    locks["Sorted advisory locks<br/>command + aggregate + wallets + quotas"]
    inbox{"Command inbox"}
    snapshots["State + wallet + quota snapshots<br/>UTC time + named entropy"]
    decide["Pure IGameAction.Decide"]
    plan["Materialized GameEffectPlan<br/>validation + grouping"]
    apply["Deterministic effect pipeline"]
    commit["Inbox result + commit"]
    delivery["Outbox dispatch<br/>events + schedules"]

    request --> envelope --> transaction --> locks --> inbox
    inbox -->|"duplicate"| commit
    inbox -->|"new"| snapshots --> decide --> plan --> apply --> commit
    commit --> delivery
```

The game action is synchronous and has no infrastructure dependencies. It sees
only the command, loaded state, snapshots, named entropy and framework time. It
returns a `GameDecision` containing the new state, public result and complete
materialized effect lists. Randomness, clocks, database reads, analytics calls,
Telegram/Discord sends and service resolution do not occur inside `Decide`.

The framework has a second game-state kernel for commands that do not cross the
economy boundary. `IGameStateExecutor<TCommand,TState,TResult>` keeps the same
transactional inbox, aggregate locking, revision and outbox guarantees, but
provides no wallet or quota snapshot and rejects economy, quota and wallet custom
effects. This lets high-throughput state transitions avoid taking economic locks
without weakening game-state consistency.

```mermaid
flowchart LR
    request["command"] --> envelope["envelope + idempotency"]
    envelope --> stateTx["state transaction"]
    stateTx --> stateLocks["command + aggregate locks"]
    stateLocks --> stateInbox["state inbox"]
    stateInbox --> stateAction["pure game action"]
    stateAction --> stateSave["state + domain events + schedules"]
    stateSave --> stateCommit["commit"]
    stateCommit --> stateOutbox["post-commit outbox"]
```

Wagered play uses this state-only kernel behind `IOutcomeOnlyGameExecutor`.
Economics is a separate durable workflow:

```text
reserve funds
  -> game state transition
  -> GameOutcomeDeclared
  -> Wagering terms/payout policy
  -> Ledger settlement or reservation release
```

The old `IAtomicGameExecutor` remains the correct boundary for commands that
must debit/credit wallets, consume quotas, enforce player protection, transfer
between wallets, or execute a legacy refund/payout in the same decision. The
current migration therefore keeps legacy Blackjack Atomic mode while the
separate `BlackjackWagerState` is outcome-only. The same split is used by the
generic single-player wager adapters, specialized Horse/Poker/Secret Hitler
adapters and multi-party Challenge/Pick workflows.

```mermaid
flowchart TB
    input["GameActionInput"]
    command["Command"]
    state["Current state"]
    wallet["WalletSnapshot"]
    quotas["QuotaSnapshot map"]
    entropy["Named EntropyValue"]
    time["UtcNow"]

    action["IGameAction.Decide<br/>pure synchronous rules"]

    decision["GameDecision"]
    newState["NewState"]
    result["Result"]
    effects["Materialized effects"]

    command --> input
    state --> input
    wallet --> input
    quotas --> input
    entropy --> input
    time --> input
    input --> action --> decision
    decision --> newState
    decision --> result
    decision --> effects
```

### Effect lanes

Effects are typed data, not callbacks, tasks, or lazy streams. Before the first
mutation, `GameEffectPlan` verifies that effects are valid for the decision,
declared quotas exist, and record/custom effect types each have exactly one
registered writer or handler.

```mermaid
flowchart LR
    decision["Accepted GameDecision"]

    protection["Player protection"]
    primary["EconomyEffect<br/>primary wallet + ledger"]
    quota["QuotaEffect<br/>consume / restore / grant"]
    aggregate["Aggregate state store"]
    records["IGameRecord writers"]
    custom["Typed custom effects<br/>including multi-wallet"]
    eventOutbox["Domain event outbox"]
    scheduleOutbox["Schedule outbox"]
    inboxResult["Serialized inbox result"]
    pg["COMMIT"]

    decision --> protection --> primary --> quota --> aggregate --> records --> custom
    custom --> eventOutbox --> scheduleOutbox --> inboxResult --> pg
```

The lanes have distinct ownership:

| Lane | Owner and invariant |
|---|---|
| `EconomyEffect` | Framework primary wallet; protected debit, balance and append-only ledger stay consistent |
| `WalletEconomyEffect` | Framework multi-wallet handler; all target wallet locks are declared before `Decide` |
| `QuotaEffect` | Framework quota store; configured capacity and usage change atomically with the command |
| aggregate state | Module state store operating through `IGameExecutionContext` |
| `IGameRecord` | Module history writer; preserves synchronous history consistency where required |
| custom `IGameEffect` | One explicitly registered typed handler; no raw connection/transaction exposure |
| `IDomainEvent` | Framework outbox; analytics and remote reactions happen after commit |
| `ScheduleEffect` | Framework schedule outbox; Quartz work is created or cancelled durably |

`IAsyncEnumerable<IGameEffect>` is intentionally not used. A lazy effect stream
could throw after partial enumeration, depend on I/O timing, or produce different
effects on retry. The decision is a finite value that can be validated before
mutation. Npgsql work inside one transaction remains sequential; concurrency is
achieved between independent commands rather than by issuing simultaneous
operations on one connection.

### Workflow effects and Quartz semantics

Workflow mutations that do not have a user-facing `IGameAction` (quest progress/claims,
tournament lifecycle transitions and season payouts) use the SDK's
`IAtomicEffect` plus Host `IAtomicEffectExecutor`. The executor takes stable locks,
checks the command inbox, resolves one typed handler per effect, and commits the
result, ledger/history changes and outbox rows in one PostgreSQL transaction.
Handlers receive a restricted `IAtomicEffectContext`; they never receive a
connection, transaction or DI container. This keeps scheduled settlement and
interactive commands on the same idempotent mutation path.

Quartz is deliberately only the trigger and persistent schedule coordinator. A
`ScheduleExecutionPolicy` is attached to a schedule rather than encoded in a job:

| Policy | Meaning |
| --- | --- |
| `Misfire` | `FireOnce` catches up once, `Ignore` replays missed fires, `DoNothing` skips them |
| `Concurrency` | `Disallow` serializes one job key; `Allow` permits overlapping runs |
| `BatchSize` | bounded work hint passed to the scheduled command as `batch-size` |
| `MaxAttempts` / `RetryBackoff` | bounded Quartz job retries before the failure is surfaced |

The command must still use an atomic/effect executor. Quartz retries therefore
retry the same idempotent command semantics, while rendering, ClickHouse analytics
and Telegram delivery remain asynchronous post-commit consumers.

### Long-lived tournament workflow

Tournament commands use the framework's `BotFramework.Host.Workflows` boundary in
the Backend. `ITournamentService` keeps the existing public contract, but command
execution is routed through `ITournamentWorkflow`; the workflow handler delegates
the actual local transition to `TournamentCommandExecutor` and the existing
`IAtomicEffectExecutor`. The framework owns the Wolverine adapter, correlation,
durable step delivery, partitioning, replay and persisted generic saga state.

After a local effect commits, the handler uses `IDurableWorkflowStepExecutor` to
emit a `DurableWorkflowStep` through the framework. `durable_workflow_steps` is an
audit and recovery projection with `workflow_id`, `command_id`, `command_type`,
`causation_id`, the full original `command_json`, result, status, terminal marker
and payload. A bounded
facade wait returns a typed result when the command finishes quickly; otherwise it
returns `Pending` for the user-facing create/join/report contracts while the durable
command continues in the background.

The split Wallet boundary is explicit. Join debits, prizes and cancellation refunds
are applied through `IWalletAtomicExecutionService` before the local tournament
transition, using stable Wallet operation ids. If the local transition is definitively
rejected, a compensating credit/debit is sent with a `.rollback` reason. If the
process fails after the Wallet step but before the local commit, the framework
retry policy retries the same command and the Wallet operation id plus AtomicEffect
inbox make the retry safe. The domain tables, AtomicEffect inbox and CAP event
outbox remain the source of domain truth; the workflow table is the operator-visible
recovery timeline.

The admin tournament page is deliberately a control plane, not a second mutation
engine: SuperAdmin can inspect command/result JSON and replay a failed or non-terminal
step. Replay reuses the original command id. Telegram/Discord client outboxes and
the CAP domain-event outbox remain unchanged.

### Wagering Saga And Settlement

Wagering is the distributed consistency boundary between a Game Service and
Wallet/Ledger. It owns the wager operation, immutable `WagerTermsSnapshot`,
hold, capture and payout operation ids, payout policy selection and the public
asynchronous status. Ledger owns balances and financial invariants. The Game
Service owns game rules, game state and the final outcome. No service writes
another service's database.

#### Single-player wager lifecycle

The normal single-player lifecycle is a durable state machine. The operation and
outbox transition are committed locally before the next integration command is
sent:

```mermaid
stateDiagram-v2
    [*] --> Pending
    Pending --> Reserving: WagerRequested accepted
    Reserving --> Rejected: reservation rejected
    Reserving --> Playing: reservation completed
    Playing --> Settling: GameOutcomeDeclared
    Settling --> Completed: settlement confirmed
    Settling --> Failed: settlement rejected
```

```mermaid
sequenceDiagram
    participant Client
    participant W as Wagering
    participant L as Ledger
    participant G as Game Service

    Client->>W: WagerRequested(operationId, betId, terms)
    W->>W: Pending -> Reserving + local outbox commit
    W-->>L: LedgerHoldRequested(op)
    L->>L: hold funds atomically and idempotently
    L-->>W: LedgerOperationCompleted(Hold)
    W->>W: Reserving -> Playing + local outbox commit
    W-->>G: addressed game command
    G->>G: state-only transaction: state + inbox + event outbox
    G-->>W: GameOutcomeDeclared(outcome, evidence)
    W->>W: validate terms and apply payout policy
    W->>W: Playing -> Settling + local outbox commit
    W-->>L: LedgerCaptureRequested(op:capture, holdId=op)
    L->>L: capture hold atomically; optionally credit house account
    L-->>W: LedgerOperationCompleted(Capture)
    W-->>L: LedgerTransferRequested(op:payout, house -> player)
    L->>L: transfer payout atomically and idempotently
    L-->>W: LedgerOperationCompleted(Transfer)
    W->>W: Settling -> Completed or Failed
    W-->>Client: WagerSettled / status projection
```

`GameOutcomeDeclared` contains no stake, balance or payout. Wagering evaluates
the outcome against the original terms snapshot and selects the game-specific
payout policy. A game result is final before settlement; capture and payout are
separate idempotent Ledger operations. The deterministic ids are
`operationId`, `operationId:capture` and `operationId:payout`, so a retry must
never reroll or rewrite game state. A zero payout still captures the hold and
finishes without creating a transfer.

The game hot path ends at the committed state/outcome outbox. It does not wait
for Capture or Transfer. The extra financial steps are asynchronous saga work;
they improve failure isolation without adding wallet work to a Blackjack,
Poker, Dice or other state-only action commit.

#### Admin workflow timeline

The admin view is a read-only composition, not another projection on the game
hot path. Given an operation id, `PostgresWagerWorkflowTimelineReader` reads
`wager_operations` (or the multi-party participant row), committed ES events
from `module_events` and game event outbox rows (by `BetId`), plus the three
generic Ledger operation rows in one `QueryMultiple` round trip. It
sorts the facts by their committed timestamps and exposes the sources as
`wagering`, `es` and `ledger`. No timeline row is synchronously inserted when a
game action executes, and no admin read is allowed to mutate game or Ledger
state.

The current single-player status enum is:

```text
Pending -> Reserving -> Playing -> Settling -> Completed
                    |          |           |
                    v          |           v
                 Rejected      |         Failed
                               (outcome is already final)
```

#### Multi-party wagers and compensation

Multi-party workflows reserve every participant before starting the game. If one
reservation is rejected, or game outcome validation/execution fails, the workflow
enters compensation and refunds every reservation that was successfully created.
Only after all participant outcomes are validated does it request settlement for
each participant.

```mermaid
stateDiagram-v2
    [*] --> pending
    pending --> reserving
    reserving --> playing: all reservations reserved
    reserving --> compensating: participant rejected
    playing --> settling: outcomes and payout policy valid
    playing --> compensating: game execution/validation failed
    settling --> completed: every settlement confirmed
    settling --> failed: settlement rejected
    compensating --> compensated: every reservation refunded
```

Compensation is not a rollback of a distributed transaction. It is a new,
idempotent Ledger command with its own operation id, recorded in workflow state.
A pending or failed financial command remains observable and is retried or moved
to operator review according to workflow policy; it is never marked successful
merely because the originating process stopped waiting.

#### Atomic versus saga decision table

| Situation | Local boundary | Distributed behavior |
| --- | --- | --- |
| Game command debits/credits, consumes quota or enforces protection | `IAtomicGameExecutor` | One local transaction contains state and economic effects |
| Game command changes state, records, events or local schedules only | `IGameStateExecutor` | One local state transaction; no wallet/quota access |
| Reserved wager starts or advances game play | `IOutcomeOnlyGameExecutor` over state executor | Commit outcome first, then Wagering requests settlement |
| Wallet/Ledger reservation or settlement | Wallet/Ledger local transaction | Wagering waits on integration events and retries idempotently |
| Multi-wallet transfer or legacy refund/payout | Atomic multi-wallet/effect executor | Keep the local economic boundary until migrated |

The current repository uses outcome-only adapters for generic Dice, DiceCube,
Darts, Football, Basketball, Bowling and Pick wagers; specialized Blackjack,
Horse, Poker and Secret Hitler wager slices; and multi-party Challenge/Pick
workflows. The old Blackjack game remains an Atomic compatibility mode beside
the separate `BlackjackWagerState` model. Pure non-economic commands in legacy
game services may use `IGameStateExecutor`, but an Atomic path is intentional
where the command still owns economic effects.

#### Delivery, retry and source of truth

```text
service-local state + inbox + outbox: PostgreSQL transaction
integration delivery: CAP Local / Redis Streams / Kafka-Redpanda
durable workflow delivery and replay: Wolverine + PostgreSQL
financial truth: Wallet/Ledger database
workflow recovery timeline: Wagering operation/workflow tables
```

CAP/Kafka/Redis is a transport, not a source of truth or a distributed
transaction coordinator. Wolverine's durable workflow store records delivery,
steps, retries and replay, but the domain aggregate and Ledger records remain
authoritative. Every integration consumer is at-least-once and therefore uses an
inbox or equivalent stable operation-id check. Outbox rows are published only
after the local transaction commits; a relay crash may duplicate delivery, never
the intended business operation.

### Generic Ledger, Risk And Case Primitives

The framework now provides a broader operations layer beside the existing Wagering
protocol. New integrations should use the generic contracts in
`BotFramework.Contracts.Ledger`, `Risk` and `Cases` rather than inventing another
wager-specific reservation protocol. The local Host
provides Postgres handlers; a split deployment may replace them with a remote
Ledger or Case service consuming the same commands and publishing the same events.

```text
LedgerHoldRequested
        |
        v
      Held
      /  \
 Capture Release
    |       |
 Captured  Released
    |
 Refund(amount)       Transfer is an independent atomic ledger operation
```

The financial semantics are deliberately explicit:

| Primitive | Meaning | Reference rule |
| --- | --- | --- |
| `Hold` | Earmarks funds and reduces available balance | creates `HoldId`; has expiry |
| `Capture` | Confirms all or part of a hold | references `HoldId`; never pays winnings |
| `Release` | Returns the uncaptured part of a hold | references `HoldId`; supports partial release |
| `Refund(amount)` | Reverses already captured funds | references the original hold/capture operation; cumulative amount cannot exceed capture |
| `Transfer` | Moves funds between accounts | one idempotent operation; local Ledger transaction |
| `Adjustment` | Signed, audited administrative correction | requires an actor and reason |

`LedgerOperationCompleted` is the common result event. Every command carries a
stable `OperationId`; retries and duplicate delivery must return the persisted
operation result. The framework's local wallet adapter currently supports the
`coins` currency, while the contracts are currency-neutral for a dedicated Ledger
service.

Risk is a policy decision, not a balance mutation:

```mermaid
flowchart LR
    evaluate["Risk Evaluate<T>"] --> allow[Allow]
    evaluate --> deny[Deny]
    evaluate --> review[Review]
    evaluate --> hold[Hold]
    review --> case[Open Case]
    hold --> ledger[Ledger Hold]
    case --> ledger
```

`IRiskEvaluator<TContext>` returns `RiskDecision` with the policy version,
reason, optional `CaseId`/`HoldId`, expiry and evidence references. The application
owns the fraud policy; the framework only transports and composes its decision.

Cases are a generic durable human-in-the-loop aggregate:

```text
Open -> Evidence -> Review -> Resolved -> Appealed
```

`PostgresCaseCommandHandler` persists the state with optimistic versioning and
publishes `CaseOpened`, `CaseEvidenceAdded`, `CaseReviewStarted`, `CaseResolved`,
`CaseAppealed` or `CaseTransitionRejected` through the configured integration
outbox. Moderation, disputes and antifraud may use the same lifecycle while
keeping their own case types, policies and evidence stores.

Typical compositions are therefore:

```text
Risk Review -> Case + Hold -> DurableWorkflow timeout -> Release
Risk Allow  -> Hold -> state-only game -> Capture + payout Transfer
Dispute     -> Case + Hold/Freeze -> Evidence -> human Resolve -> Refund/Release
Antifraud   -> RiskDecision -> Hold or Case -> Capture/Release
```

Timeout is an orchestration concern: a durable workflow schedules an idempotent
`LedgerReleaseRequested` for an expired hold. Ledger still stores `ExpiresAt` and
enforces the hold state, so a late release or duplicate timeout cannot create a
second credit. `Refund` remains distinct from `Release`, which prevents a dispute
workflow from refunding more than was captured.

### Aggregate and lock scope

The descriptor maps a command to its consistency boundary. Commands sharing any
lock serialize; commands with disjoint lock sets can execute in parallel.

```mermaid
flowchart TD
    command["Command"]
    descriptor["GameExecutionDescriptor"]

    userKey["user game<br/>game:{chat}:{user}"]
    tableKey["turn-based table<br/>table:{inviteCode}"]
    tileKey["PixelBattle cell<br/>tile:{index}"]
    walletKeys["all affected wallet keys"]
    quotaKeys["declared quota keys"]

    command --> descriptor
    descriptor --> userKey
    descriptor --> tableKey
    descriptor --> tileKey
    descriptor --> walletKeys
    descriptor --> quotaKeys
```

Examples:

- Dice uses a user/game aggregate plus its wallet and daily quota.
- Challenges, Transfer, Horse and completed table games declare every debit or
  payout wallet, allowing a single atomic multi-wallet commit.
- Blackjack, Poker and Secret Hitler serialize transitions by table/game id.
- PixelBattle uses a tile aggregate. Writes to one tile are ordered while writes
  to different tiles remain parallel; the full grid is a read model, not a
  global mutation lock.

### Commit, retry and external work

```mermaid
sequenceDiagram
    participant Transport
    participant Executor
    participant Inbox
    participant Action
    participant PG as PostgreSQL
    participant Outbox

    Transport->>Executor: command + stable commandId
    Executor->>PG: BEGIN + sorted advisory locks
    Executor->>Inbox: GetOrBegin(commandId)
    alt completed duplicate
        Inbox-->>Executor: stored TResult
        Executor->>PG: COMMIT
        Executor-->>Transport: same TResult
    else new command
        Executor->>Action: Decide(materialized input)
        Action-->>Executor: GameDecision
        Executor->>PG: state + effects + inbox result + outbox rows
        Executor->>PG: COMMIT
        Executor-->>Transport: TResult
        Outbox-->>Outbox: deliver events/schedules asynchronously
    end
```

Cancellation or failure before commit rolls back every transactional lane. If a
response is lost after commit, retrying the same command id returns the stored
result without repeating effects. A fresh command id is a new command, so the
stable id should come from Telegram update identity, Discord interaction/component
identity, gRPC metadata, or an HTTP idempotency header.

Analytics, ClickHouse writes, CAP publication, Telegram/Discord notifications and other
remote operations are not transactional custom effects. They consume committed
domain events/outbox records. Live transports may broadcast after the executor
returns; PixelBattle additionally sends periodic full-grid snapshots so an
ephemeral SSE broadcast loss self-heals.

The legacy `IEconomicsService` remains a compatibility facade for non-command
and administrative paths. New game mutations should use the execution kernel;
read-only queries should continue to use owning-context read services directly.

## Render Runtime And Match Media

GIF and image generation is isolated from the atomic executor. The executor
commits rules, balances, state and effects before any CPU-heavy render begins.
Both deployment shapes compose `BotFramework.Rendering`; split processes share
the same content-addressed MinIO bucket.

```mermaid
flowchart LR
    result["Committed game result"]
    spec["Immutable render spec"]
    key["Renderer + version<br/>canonical content hash"]
    inflight{"In-process<br/>in-flight hit?"}
    minio{"MinIO artifact<br/>exists?"}
    iq["Interactive TPL queue"]
    bq["Background / prewarm<br/>TPL queue"]
    slots["Shared CPU semaphore<br/>MaxParallelism"]
    renderer["IRenderJob.RenderAsync"]
    artifact[("Immutable GIF / PNG")]
    history[("JSON history manifest")]

    result --> spec --> key --> inflight
    inflight -->|"yes"| artifact
    inflight -->|"no"| minio
    minio -->|"hit"| artifact
    minio -->|"interactive miss"| iq
    minio -->|"prewarm miss"| bq
    iq --> slots
    bq --> slots
    slots --> renderer --> artifact
    artifact --> history
```

The two bounded `ActionBlock` queues provide backpressure and priority separation,
while one semaphore enforces the process-wide render CPU budget. Identical work
inside a process shares one task. Across processes the stable object key makes
duplicate output harmless and subsequent requests become cache hits.

```mermaid
sequenceDiagram
    participant Quartz
    participant Queue as TPL render queue
    participant Store as MinIO
    participant Game as Live game request

    Quartz->>Queue: Horse winner × variant matrix
    loop bounded prewarm
        Queue->>Store: find(content key)
        alt missing
            Queue->>Queue: render in shared CPU slot
            Queue->>Store: put immutable GIF
        end
    end
    Game->>Queue: selected deterministic variant
    Queue->>Store: find(content key)
    Store-->>Game: cached GIF
```

Horse prewarming is a Quartz recurring scheduled command, not an unbounded
background loop. Poker board images use on-demand content deduplication. Match
history stores manifests referencing immutable objects, so repeated views do not
duplicate media bytes. Admin history/artifact reads are exposed only under the
existing protected `/admin` surface.

MinIO is deliberately outside PostgreSQL transactions. If it is unavailable,
the current request receives a transient freshly rendered artifact, storage
failures are metered/logged, and the committed game result is not rolled back.
Artifact history therefore has operational durability, not domain consistency;
PostgreSQL remains the source of truth for wallets, ledgers and match state.

## Wallet And Ledger

The logical wallet/protection ports live in `BotFramework.Contracts`. Identity uses
the separate `IPlayerDirectory` port. Composition chooses their implementation:

- combined host/backend: local PostgreSQL services;
- split deployment: `CasinoShiz.IdentityService` and `CasinoShiz.WalletService`
  reached through adapters in `*.Transport.Grpc`;
- Telegram and Discord BFFs: always consume the logical ports and do not know
  whether their target is the main backend or a dedicated service.

Backend selection is configured with `Services:{Identity|Wallet}:Mode` (`Local` or
`Grpc`) and `Address`. The BFF accepts independent
`Services:{Identity|Wallet}:Address` values and falls back to
`Backend:GrpcAddress`. Transport choice therefore stays in composition roots.

Wallet account reads now cross `IWalletReadService`; ledger and operational
aggregates cross `IWalletAnalyticsService`. Game contexts no longer query the
compatibility `users` or `economics_ledger` tables. SQL for those tables is confined
to wallet-owned infrastructure. Legacy Razor admin pages are compiled into Backend
as a compatibility surface. In a split deployment the browser reaches them only
through Admin BFF, so the existing operator UI remains intact while pages migrate
to owning-context contracts incrementally.

### Physical data ownership

The microservices deployment makes the logical boundaries physical. Each service
has its own PostgreSQL database and migration set:

```mermaid
flowchart LR
    backend["Backend / game services"] --> backendDb[("backend PostgreSQL")]
    identity["Identity service"] --> identityDb[("identity PostgreSQL")]
    wallet["Wallet service"] --> walletDb[("wallet PostgreSQL")]
    backend -. "Identity gRPC" .-> identity
    backend -. "Wallet gRPC" .-> wallet
    admin["Admin BFF / aggregation"] -. "gRPC composed reads" .-> backend
    admin -. "gRPC composed reads" .-> identity
    admin -. "gRPC composed reads" .-> wallet
```

Backend may read only Backend-owned tables in microservices mode. Identity and
Wallet start with their own connection strings and execute only their migrations;
neither service depends on the compatibility `cazino` database. The monolith
profile intentionally keeps the existing shared `postgres/cazino` database for
backward compatibility, while service boundaries remain contract-level.

No cross-database joins or shared-table reads are allowed. A player or admin view
that needs several contexts is a composed read model assembled by Admin BFF from
Identity, Wallet and Backend APIs. Writes follow the same rule: the owning service
validates and mutates its own data, and other services receive commands or events.

## Admin BFF

`CasinoShiz.AdminBff` owns browser login/session state and registers no database
connection. After authentication it reverse-proxies `/admin/*` (except login and
logout) to Backend, adding the internal operations key and authenticated actor.
Backend validates those headers, creates the legacy server session, and executes
the original Razor pages and their antiforgery-protected actions.

The proxy implementation is YARP. External gRPC clients are created through the
shared resilient client factory in `CasinoShiz.ServiceDefaults`; composition roots
also use that project for OpenTelemetry and common health registration.

Admin BFF is also the aggregation boundary for cross-service read models. Typical
views are composed through APIs such as:

```text
GET /api/aggregation/players/{userId}
  -> Identity player/profile data
  -> Wallet balance/protection data

GET /api/aggregation/admin
  -> Backend operations data
  -> Wallet analytics data
```

The response is assembled in the BFF from gRPC clients. It is not a SQL join and
does not expose another service's tables. Backend, Identity, Wallet and both
channel BFFs expose liveness/readiness checks; readiness includes only the
dependencies owned or required by that process.

The first migrated vertical slice contains wallet inspection, idempotent balance
adjustment and identity lookup. Each rendered adjustment carries a stable operation
ID, so repeating the same browser POST cannot apply the ledger mutation twice.
Service failures are reported per page rather than taking down unrelated sections.

The old `CasinoShiz.Host/Pages/Admin` remains the single compatibility UI source and
is linked into Backend at build time; it is not duplicated in AdminBff. A page can
later move behind an owning-context contract after its read/mutation contract and
server-side audit semantics exist.

The current compatibility wallet identity is `(telegram_user_id, balance_scope_id)`.
Identity maps external channel identities to the logical player id; the balance
scope is normally the originating chat/server scope, so one person can have
independent balances in different groups, servers and private chats.

```mermaid
sequenceDiagram
    participant Game as Game service
    participant Player as /mystats
    participant Protect as PlayerProtectionService
    participant Econ as EconomicsService
    participant PG as PostgreSQL

    Player->>Protect: inspect stats or configure protection
    Protect->>PG: read/upsert player_protection
    Game->>Econ: TryDebitOnce(user, chat, amount, operationId)
    Econ->>PG: BEGIN
    Econ->>PG: advisory lock + read player_protection
    alt cooldown, exclusion, or daily limit blocks wager
        Econ->>PG: ROLLBACK
        Econ-->>Game: PlayerProtectionException
    else wager allowed
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
    end
```

Important guarantees:

- `SELECT ... FOR UPDATE` serializes concurrent mutations to one wallet;
- wallet update and ledger append happen in one transaction;
- operation ids make critical debits, credits, transfers, refunds, and prizes idempotent;
- the ledger is append-only; admin recovery writes compensating rows.
- `PlayerProtectionService` reads player statistics and configures daily stake limits,
  cooldowns, and self-exclusion through `/mystats`;
- `EconomicsService` enforces those controls transactionally before protected wager
  mutations, while administrative, transfer, and rollback reasons are exempt.

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
    mode{"Messaging:Transport"}

    inproc["InProcessEventBus<br/>sequential, same process"]
    cap["CapEventBus"]
    outbox[("PostgreSQL CAP outbox")]
    transport[("CAP transport<br/>Redis Streams or Kafka/Redpanda")]
    consumer["CapEventConsumer"]
    subscribers["Pattern-matched subscribers"]

    dispatcher --> mode
    mode -->|"Local"| inproc
    inproc --> subscribers
    mode -->|"Redis or Kafka"| cap
    cap --> outbox
    outbox --> transport
    transport --> consumer
    consumer --> subscribers
```

Subscriptions use event-name patterns such as `sh.game_ended`, `sh.*`,
`*.game_ended`, or `*`. Subscribers must be idempotent because distributed
delivery is at least once.

In distributed mode the transactional source is the Backend-owned
`game_event_outbox`. A lease-based dispatcher publishes committed rows through
the CAP PostgreSQL outbox into the selected Redis Streams or Kafka/Redpanda
transport; consumers then update only their local projections or call another
service API. `Backend:ServiceName` determines
the logical consumer group: replicas of `game-poker` share one group and load
balance, while `game-poker` and `game-dice` receive independent fan-out copies.

Schedules use the same transactional boundary but different ownership. Every
`game_schedule_outbox` row carries `game_id`; a worker claims only rows for
modules loaded by its service. Quartz uses `Backend:ServiceName` as the persistent
`sched_name` partition, so replicas of one game cluster together and cannot acquire
another game's triggers. The monolith keeps the legacy `CasinoShiz` scheduler name.

## Telegram Outbox

Critical Telegram messages emitted outside the live update response path are
persisted before sending. This covers event subscribers and background jobs where
`DB/event -> Telegram side effect` should survive process restarts and transient
Telegram failures.

```mermaid
flowchart LR
    subscriber["Event subscriber / job"]
    api["ITelegramOutbox"]
    table[("telegram_outbox")]
    mode{"TelegramOutbox:Transport"}
    dispatcher["Local dispatcher"]
    relay["Backend CAP relay"]
    cap["CAP transport<br/>Redis Streams or Kafka/Redpanda"]
    bff["Telegram BFF"]
    bot["Telegram Bot API"]

    subscriber -->|"EnqueueAsync<br/>optional dedupe_key"| api
    api --> table
    table --> mode
    mode -->|"Local"| dispatcher
    mode -->|"Cap"| relay
    dispatcher -->|"claim due rows<br/>FOR UPDATE SKIP LOCKED"| table
    dispatcher -->|"SendMessage"| bot
    bot -->|"message id"| dispatcher
    dispatcher -->|"mark sent / schedule retry"| table
    relay -->|"claim due rows"| table
    relay -->|"delivery command"| cap
    cap --> bff
    bff -->|"SendMessage"| bot
    bff -->|"delivery receipt"| cap
    cap -->|"mark sent"| table
```

`dedupe_key` suppresses duplicate enqueues from repeated event handling. Both
delivery modes reclaim expired leases; the CAP mode records `telegram_message_id`
only after the Telegram BFF receipt. Telegram itself has no idempotency key, so
notifications must still tolerate at-least-once delivery. Live handler replies,
validation errors, menus, and other immediate user interactions still use direct
`ctx.Bot.SendMessage(...)` calls.

## Discord Outbox

Discord interaction responses can be immediate, but notifications and deferred
messages are persisted in `discord_outbox` before delivery. Discord BFF replicas
claim due rows with leases, retry transient failures, and preserve a deduplication
key. This keeps delivery independent from the interaction request and makes the
same outbox safe for combined, split and horizontally scaled deployments.

```mermaid
flowchart LR
    handler["Discord handler / event subscriber"]
    outbox["discord_outbox"]
    claim["lease claim<br/>FOR UPDATE SKIP LOCKED"]
    bff["Discord BFF replica"]
    api["Discord API"]
    retry["retry metadata / expired lease"]

    handler --> outbox --> claim --> bff --> api
    api -->|"success"| outbox
    bff -->|"transient failure"| retry --> outbox
```

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
    registry["Registered typed sections"]
    strict["Strict JSON merge<br/>unknown fields rejected"]
    validation["IConfigurationValidator&lt;T&gt;<br/>FluentValidation adapter"]
    adminEffects["AdminEffectPlan<br/>transaction + mandatory audit"]
    table[("runtime_tuning.payload")]
    accessor["RuntimeTuningAccessor"]
    effective["Effective typed options"]
    games["Game/framework services"]

    file --> accessor
    env --> accessor
    registry --> strict
    admin --> strict
    strict --> validation
    validation --> adminEffects
    adminEffects --> table
    table --> accessor
    accessor --> effective
    effective --> games
```

Precedence is:

1. `appsettings.json`;
2. environment variables;
3. whitelisted database patch.

`BindOptions<TOptions, TValidator>` registers one section for both startup and
runtime validation. The SDK owns only `IConfigurationValidator<TOptions>` and
structured issues; the Host supplies a FluentValidation bridge and
`ValidateOnStart`. Admin JSON is not a loose property bag: the registry rejects
unregistered sections, strict deserialization rejects unknown nested properties,
and semantic validators run against the fully merged effective options. Preview
and apply therefore share exactly the same validation path.

Saving from `/admin/settings` produces a `RuntimeConfigurationPatchEffect`. The
admin execution kernel applies that effect and appends `admin_audit` in the same
PostgreSQL transaction; after commit the runtime accessor reloads the normalized
patch. Services that read `IRuntimeTuningAccessor` receive changes without process
restart. Failed validation performs no writes, and a failed effect rolls back both
the mutation and audit.

## Administrative Effect Coverage

Administrative commands use the same finite, typed-effect discipline as games. The
Razor pages and `Games.Admin` Telegram service build an `AdminExecutionEnvelope`
and `AdminEffectPlan<TResult>`; they do not open their own mutation connection or
transaction. The Host resolves one handler per effect, applies it with the current
transaction-aware context, appends `admin_audit`, and commits once.

```mermaid
flowchart LR
    users["Users page"] --> wallet["WalletAdjustmentAdminEffect"]
    ledger["Ledger page"] --> revert["LedgerRevertAdminEffect"]
    admin["Games.Admin"] --> adminEffects["ClearChatBets / DisplayNameOverride"]
    meta["Meta seasons / alerts / quests"] --> metaEffects["MetaSeason* / MetaAlertStatus / MetaQuestCatalog*"]
    wallet --> executor["AdminEffectExecutor<br/>one PostgreSQL transaction"]
    revert --> executor
    adminEffects --> executor
    metaEffects --> executor
    executor --> state[("wallets, ledger, game/meta tables")]
    executor --> history[("meta_event_log")]
    executor --> audit[("admin_audit")]
```

The migrated administrative surface is intentionally explicit:

- Users adjusts a wallet and appends its ledger line; `operation_id` makes retries
  idempotent.
- Ledger reversal locks the source and wallet rows, checks prior compensation, and
  writes the correction atomically.
- Games.Admin clears pending DiceCube/Darts/Football/Basketball/Bowling bets with
  refunds, or changes a display-name override, without a second mutation path.
- Meta season creation/preparation/activation/finish/configuration and player/clan
  rewards are effects. Rewards use deterministic operation ids and write
  `meta_event_log` in the same transaction. Alert status changes follow the same
  history path.
- Quest catalog save/reload are effects as well. Save uses a temp-file + atomic
  replace for the existing file-backed catalog; that filesystem operation is not
  rollbackable by PostgreSQL, so a database/object-store catalog remains the next
  hardening step.

Read-only queries may continue to use the existing stores and Dapper projections.
They are deliberately outside the executor and therefore cannot accidentally
create a partial admin mutation.

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
    economy["Current economy snapshot"]
    jobs["Read-only background-job status"]
    recovery["/admin/recovery<br/>event + outbox records"]
    auditPage["/admin/audit<br/>CSV / JSON downloads"]

    token["Token form or<br/>Telegram Login Widget"]
    session["AdminSession<br/>SuperAdmin / ReadOnly"]
    audit[("admin_audit")]
    pg[("PostgreSQL operational state")]
    jobState["In-process job status snapshots"]

    request --> path
    path --> health
    path --> webhook
    path --> pixel
    path --> adminGate
    adminGate --> login
    login --> token
    token --> session
    adminGate -->|"authenticated"| pages
    pages --> economy
    pages --> jobs
    pages --> recovery
    pages --> auditPage
    economy --> pg
    jobs --> jobState
    recovery --> pg
    auditPage --> audit
    adminPlan["Typed AdminEffectPlan"]
    adminExecutor["Admin effect executor<br/>one transaction"]

    pages -->|"write actions"| adminPlan
    adminPlan --> adminExecutor
    adminExecutor --> pg
    adminExecutor --> audit
```

Read-only admins can inspect operational pages but mutation handlers return `403`.
SuperAdmin writes include balance changes, race actions, runtime tuning, and other
administrative operations. The dashboard economy snapshot and background-job table
are read-only operational views. `/admin/audit` returns browser downloads in CSV or
JSON while keeping the same role boundary as the on-screen audit view.

## Deployment Topology

### Docker Compose

The compose file exposes three application profiles. `monolith` runs
`CasinoShiz.Host` against the existing shared `postgres/cazino` database;
`microservices` runs Backend, Identity, Wallet, TelegramBff, DiscordBff, and
AdminBff as separate containers with physical service databases; `distributed`
runs one module-selected Backend per game and channel-specific BFFs. The
monolith and legacy microservices profiles are preserved. Internal calls use
service DNS names and gRPC; only composition and environment values differ
between deployment modes.

```bash
docker compose --profile monolith up --build
docker compose --profile microservices up --build
docker compose --profile distributed up --build
docker compose --profile microservices config
```

The last command is a safe topology check: it must resolve
`backend-postgres`, `identity-postgres` and `wallet-postgres` with independent
named volumes before the profile is started.

```mermaid
flowchart TB
    internet["Telegram / Discord / browser"]

    subgraph compose["Docker Compose network"]
        bot["Telegram + Discord BFFs"]
        postgres[("PostgreSQL 16<br/>profile-owned databases")]
        redis[("Redis")]
        cap[("CAP transport<br/>Redis Streams / Kafka-Redpanda")]
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
    bot <--> cap
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

### Compose profile ownership and scaling

| Profile | Application shape | PostgreSQL ownership |
|---|---|---|
| `monolith` | `CasinoShiz.Host` with all selected modules and Telegram/Discord adapters | shared `postgres/cazino` for compatibility |
| `microservices` | all-games Backend + Identity + Wallet + channel BFFs + Admin BFF | `backend-postgres/backend`, `identity-postgres/identity`, `wallet-postgres/wallet` |
| `distributed` | one module-selected Backend per game + channel BFFs + Admin BFF | Backend game services share `backend-postgres`; Identity and Wallet retain their own databases |

The microservices profile enforces physical data ownership. Backend, Identity and
Wallet have separate connection strings, health checks and migration sets. BFFs
and Backend do not connect to another service's database; aggregation is performed
over gRPC APIs.

The distributed profile reuses the same Backend image and selects a module at
startup. For example:

```yaml
Backend__Modules: dice
Backend__ServiceName: game-dice
ConnectionStrings__Postgres: Host=backend-postgres;Database=backend
```

Channel BFFs route each logical game through `Backend:GameAddresses:<GameId>` and
fall back to `Backend:GrpcAddress` for a monolith or single Backend. Stable Compose
DNS names make replica changes an infrastructure operation:

```bash
docker compose --profile distributed up --build --scale game-poker=3
```

All Backend replicas use PostgreSQL advisory migration locks. Event outbox rows are
leased with `SKIP LOCKED`; schedule rows are claimed only by the service that owns
their `game_id`; Quartz partitions triggers by `Backend:ServiceName`. Therefore
replicas load-balance one logical game service without executing another game's
migrations or scheduled work.

### Kubernetes / Helm

```mermaid
flowchart TB
    channels["Telegram / Discord"]
    ingress["Ingress"]

    subgraph cluster["Kubernetes cluster"]
        telegramBff["Telegram BFF<br/>Deployment + Service"]
        discordBff["Discord BFF<br/>Deployment + Service"]
        adminBff["Admin BFF<br/>Deployment + Service"]
        games["game-* Deployments + Services<br/>one per selected module"]
        backendSvc["Backend PostgreSQL Service"]
        backendState[("Backend PostgreSQL StatefulSet")]
        identitySvc["Identity PostgreSQL Service"]
        identityState[("Identity PostgreSQL StatefulSet")]
        walletSvc["Wallet PostgreSQL Service"]
        walletState[("Wallet PostgreSQL StatefulSet")]
        rdsvc["Redis Service"]
        rdstate[("Redis StatefulSet")]
        cap["CAP transport<br/>Redis Streams / Kafka-Redpanda"]
        secret["Kubernetes Secret"]
    end

    channels --> ingress
    ingress --> telegramBff
    ingress --> discordBff
    ingress --> adminBff
    telegramBff --> games
    discordBff --> games
    adminBff --> games
    games --> backendSvc
    backendSvc --> backendState
    games --> identitySvc
    identitySvc --> identityState
    games --> walletSvc
    walletSvc --> walletState
    games --> rdsvc
    rdsvc --> rdstate
    games --> cap
    secret --> telegramBff
    secret --> discordBff
    secret --> adminBff
    secret --> games
```

The Helm chart uses the same image/configuration model as the distributed Compose
profile. Each game is a Deployment/Service pair with `Backend:Modules` and
`Backend:ServiceName`; scaling a game changes only its replica count. Identity,
Wallet and Backend use separate PostgreSQL StatefulSets/Services and volumes.
Redis carries update streams and distributed coordination. CAP integration
delivery may use Redis Streams or an external Kafka/Redpanda cluster; Local is
available for in-process development. ClickHouse, MinIO and the monitoring stack
may be external or disabled by default.

The chart can be installed without changing application code:

```bash
helm upgrade --install cazinoshiz ./deploy/helm/cazinoshiz
kubectl scale deployment game-poker --replicas=3
```

Managed PostgreSQL, Redis and Kafka/Redpanda can replace the chart-managed
dependencies by overriding service addresses, credentials and storage values.
Backups must be configured per owned database; restoring Backend, Identity or
Wallet does not require cross-database joins.

## Failure And Recovery Boundaries

```mermaid
flowchart LR
    updateFailure["Update processing failure"]
    eventFailure["Projection/event dispatch failure"]
    telegramFailure["Async Telegram send failure"]
    discordFailure["Async Discord send failure"]
    infraFailure["Optional analytics failure"]

    updateRetry["Redis pending retry<br/>or polling retry"]
    updateDlq["Update DLQ"]
    failureStore[("event_dispatch_failures")]
    telegramOutbox[("telegram_outbox")]
    discordOutbox[("discord_outbox")]
    retry["Admin/debug retry"]
    replay["Event replay / projection rebuild"]
    telegramRetry["Local dispatcher or CAP relay retry"]
    discordRetry["Discord BFF lease retry"]
    adminRecovery["/admin/recovery<br/>SuperAdmin confirmation"]
    eventRetry["IEventDispatchRetryService"]
    outboxNow["Reschedule unsent row now"]
    audit[("admin_audit")]
    graceful["Log and continue<br/>core gameplay remains available"]

    updateFailure --> updateRetry
    updateRetry -->|"attempts exhausted"| updateDlq
    eventFailure --> failureStore
    failureStore --> retry
    failureStore --> replay
    telegramFailure --> telegramOutbox
    discordFailure --> discordOutbox
    telegramOutbox --> telegramRetry
    discordOutbox --> discordRetry
    failureStore --> adminRecovery
    telegramOutbox --> adminRecovery
    discordOutbox --> adminRecovery
    adminRecovery -->|"single confirmed event"| eventRetry
    eventRetry --> failureStore
    adminRecovery -->|"single pending/sending row"| outboxNow
    outboxNow --> telegramOutbox
    adminRecovery --> audit
    infraFailure --> graceful
```

PostgreSQL is the primary consistency boundary. Redis improves coordination and
distributed delivery. ClickHouse and dashboards are operationally useful but do not
own game or wallet state. Recovery is deliberately record-by-record: authenticated
admins may inspect up to 100 current failures, but only SuperAdmin may confirm a
retry. Event retry redispatches the persisted event. Outbox recovery preserves the
payload, attempt count, deduplication key, and previous error while making an unsent
record immediately eligible; sent, missing, or concurrently changed records are not
mutated. The same lease/retry model applies to Telegram and Discord delivery.
Successful mutation attempts are appended to `admin_audit`.

The browser admin is a separate `CasinoShiz.AdminBff` process and never reads
PostgreSQL directly. It uses transport-neutral Identity, Wallet, and Operations
contracts; gRPC is an adapter selected only by composition. `Admin:ReadOnlyToken`
creates an inspection-only session and `Admin:SuperAdminToken` creates a session
allowed to submit antiforgery-protected mutations. The Admin BFF and Backend must
receive the same non-empty `Services:Operations:ApiKey` from deployment secrets;
the Backend rejects every Operations gRPC call when the key is absent or invalid.
Actor ID and name come from the authenticated server-side session, while the
Backend owns execution and audit recording. Wallet adjustments therefore remain
audited even when Wallet runs as a separate service.

## Dependency Rules

```mermaid
flowchart BT
    host["CasinoShiz.Host"]
    telegramBff["CasinoShiz.TelegramBff"]
    discordBff["CasinoShiz.DiscordBff"]
    adminBff["CasinoShiz.AdminBff"]
    game["Games.* modules"]
    telegramChannel["Games.*.Telegram"]
    discordChannel["Games.*.Discord"]
    frameworkHost["BotFramework.Host"]
    telegramFramework["BotFramework.Telegram"]
    discordFramework["BotFramework.Discord"]
    sdk["BotFramework.Sdk"]

    host --> game
    host --> frameworkHost
    telegramBff --> telegramChannel
    discordBff --> discordChannel
    adminBff --> frameworkHost
    game --> frameworkHost
    telegramChannel --> telegramFramework
    discordChannel --> discordFramework
    game --> sdk
    frameworkHost --> sdk
    telegramFramework --> frameworkHost
    discordFramework --> frameworkHost
```

- `BotFramework.Sdk` contains contracts and shared event vocabulary.
- `BotFramework.Host` implements infrastructure and cross-cutting services.
- each `Games.*` project owns one bounded feature/module;
- `CasinoShiz.Host` selects modules and maps distribution-specific endpoints;
- `CasinoShiz.TelegramBff`, `CasinoShiz.DiscordBff` and `CasinoShiz.AdminBff` are
  composition roots, not data owners;
- modules should communicate through SDK contracts and domain events rather than
  importing another game's internals.
