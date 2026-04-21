# CasinoShiz

A Telegram casino/gambling mini-game bot. Russian-language UI. Games: slots (🎰), dice cube (🎲), darts (🎯), bowling (🎳), basketball (🏀), horse racing, Texas Hold'em poker, blackjack, Secret Hitler (🗳), freespin code redemption.

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 (preview SDK) |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | **PostgreSQL 16** via Dapper (balance hot path with `SELECT ... FOR UPDATE`). No EF Core in live tree. |
| Migrations | Dapper-based, tracked in `__module_migrations`, applied at startup by `ModuleMigrationRunner` |
| Event bus | DotNetCore.CAP 10.x — PostgreSQL outbox + Redis transport when `Redis:Enabled=true`; `InProcessEventBus` fallback for single-instance / dev |
| Update fan-out | Redis Streams (opt-in via `Redis:Enabled`) — partitioned by `chatId % N`, consumer groups |
| Analytics | ClickHouse 24.x via `ClickHouse.Client` 7.x (buffered, degrades gracefully) |
| Dashboards | Grafana 11 with auto-provisioned ClickHouse datasource |
| Graphics | SkiaSharp 3.x (horse race GIF renderer, offloaded to thread pool) |
| Tests | xUnit, 631 tests covering domain + services + router + framework |
| Deploy | Docker Compose (bot + postgres + redis + clickhouse + grafana) / Helm chart |

UTC+7 is used for "day" resets (`HorseTimeHelper.GetRaceDate`).

## Layout

```
CasinoShiz/
├── docker-compose.yml                — bot + postgres + redis + clickhouse + grafana
├── Dockerfile                        — dotnet/sdk:10.0-preview multi-stage
├── CasinoShiz.slnx                   — solution manifest
├── .env.example                      — required env vars template
├── data/                             — volumes (postgres, redis, clickhouse)
├── grafana/                          — datasource + dashboards provisioning
├── helm/                             — Kubernetes Helm chart
│   ├── values.yaml                   — default values (safe to commit)
│   ├── values.secret.yaml            — secrets (git-ignored, never commit)
│   └── templates/                    — deployment, statefulsets, services, ingress, secret
├── docs/docs.md                      — this document
├── framework/
│   ├── BotFramework.Sdk/             — module contracts: IModule, IUpdateHandler, route attrs,
│   │                                   IEconomicsService, IAnalyticsService, IDomainEventBus, …
│   ├── BotFramework.Sdk.Testing/     — xUnit helpers for pure-domain tests
│   └── BotFramework.Host/            — ASP.NET host, pipeline/router, economics, analytics,
│                                       CAP event bus, Redis Streams, admin auth, migrations runner
├── games/
│   ├── Games.Dice/                   — slots 🎰
│   ├── Games.DiceCube/               — dice cube 🎲
│   ├── Games.Darts/                  — darts 🎯
│   ├── Games.Bowling/                — bowling 🎳
│   ├── Games.Basketball/             — basketball 🏀
│   ├── Games.Blackjack/              — blackjack
│   ├── Games.Horse/                  — horse racing + GIF renderer
│   ├── Games.Poker/                  — Texas Hold'em (full DDD split)
│   ├── Games.SecretHitler/           — hidden-role social deduction (full DDD split)
│   ├── Games.Redeem/                 — freespin codes + emoji captcha
│   ├── Games.Leaderboard/            — /top, /balance
│   └── Games.Admin/                  — admin Telegram commands
├── host/
│   └── CasinoShiz.Host/              — composition root
│       ├── Program.cs                — AddBotFramework().AddModule<T>()…Build().UseBotFramework()
│       ├── appsettings.json
│       └── Pages/Admin/              — Razor pages for /admin UI
└── tests/
    └── CasinoShiz.Tests/             — 631 xUnit tests
```

## Architecture

### Host composition

`BotFrameworkBuilder.AddBotFramework()` registers framework singletons (Telegram client, router, pipeline, middlewares, economics, event bus, Redis Streams, admin auth, migrations runner). Each `.AddModule<T>()` instantiates the module, runs `ConfigureServices(IModuleServiceCollection)`, and folds its handlers/locales/migrations/commands/admin pages into the shared aggregate. `UseBotFramework()` wires the webhook endpoint, health endpoints, session middleware, and admin gate.

### Request flow

```
Telegram ─► BotHostedService (polling  OR  webhook POST /{token})
         │     [if Redis:Enabled] → UpdateStreamPublisher → Redis Stream
         │     [else]             → inline dispatch
         └─► UpdatePipeline.InvokeAsync
              ├─ ExceptionMiddleware    — catch + log
              ├─ LoggingMiddleware      — structured scope: update_id/user_id/chat_id, duration
              ├─ RateLimitMiddleware    — per-user/per-chat token bucket
              └─ UpdateRouter.DispatchAsync
                   attribute-scanned routes, first-match dispatch
                   └─► IUpdateHandler.HandleAsync
                        └─► feature service (DiceService, PokerService, …)
                             ├─► INpgsqlConnectionFactory (Dapper) for domain writes
                             ├─► IEconomicsService (Dapper + FOR UPDATE) for balance
                             └─► IAnalyticsService → ClickHouse batch
```

When `Redis:Enabled=true`, the polling loop and webhook both publish updates to Redis Streams instead of dispatching inline. `UpdateStreamWorkerService` reads from N partitioned streams (keyed by `chatId % N`) in consumer groups and invokes the same `UpdatePipeline`.

### Event bus

`IDomainEventBus.PublishAsync(IDomainEvent)` — cross-module domain events.

- **Redis enabled**: DotNetCore.CAP with PostgreSQL outbox + Redis Streams transport. Guarantees at-least-once delivery across pods. `CapEventBus` resolves a scoped `ICapPublisher` per publish call via `IServiceScopeFactory`. `CapEventConsumer` receives all events on the `"domain.event"` topic and dispatches to pattern-matched subscribers.
- **Redis disabled**: `InProcessEventBus` — in-memory, sync dispatch, single-process only. Fine for dev / single-pod.

### Router (attribute-based)

Routes are declared on handler classes, not in a central table. `UpdateRouter` scans for `IUpdateHandler` implementations at startup and builds a priority-sorted dispatch list.

| Attribute | Matches | Priority |
|---|---|---|
| `[ChannelPost]` | `Update.ChannelPost != null` | 300 |
| `[MessageDice(emoji)]` | native Telegram dice with given emoji | 250 |
| `[CallbackPrefix(prefix)]` | `CallbackQuery.Data` starts with prefix | 200 |
| `[Command(prefix)]` | `Message.Text` starts with prefix | 100 + prefix.Length |
| `[CallbackFallback]` | any remaining `CallbackQuery` | 1 |

Longer command prefix wins (`/horserun` > `/horse`). Adding a handler: implement `IUpdateHandler`, decorate with route attribute, register via `services.AddHandler<T>()` in owning module's `ConfigureServices`. No host edit needed.

### Concurrency

Stateful game writes serialized with `SemaphoreSlim(1,1)` per game instance stored in a `ConcurrentDictionary<TKey, Gate>`. The `Gate` wrapper tracks `LastUsedTick` for cleanup. Stale gates are pruned by existing sweeper jobs:

- Poker: `PokerTurnTimeoutJob.SweepAsync` calls `PokerService.PruneGates(3 × turnTimeoutMs)`
- Blackjack: `BlackjackHandTimeoutJob.SweepAsync` calls `BlackjackService.PruneGates(handTimeoutMs)`
- SecretHitler: `SecretHitlerGateCleanupJob` runs every 10 min, prunes gates idle > 1 hour

Balance mutations acquire a Postgres row lock via `SELECT ... FOR UPDATE` inside `EconomicsService`.

## Economics (balance bounded context)

`IEconomicsService` is the **only** place balances mutate. All code paths go through it:

```csharp
await economics.EnsureUserAsync(userId, displayName, ct);           // insert on first update, update display name
await economics.DebitAsync(userId, amount, "horse.bet", ct);        // throws InsufficientFundsException
await economics.TryDebitAsync(userId, amount, "poker.join", ct);    // bool: did it succeed?
await economics.CreditAsync(userId, payout, "blackjack.settle", ct);
await economics.AdjustUncheckedAsync(userId, delta, ct);            // allows negative balance — admin only
```

`EnsureUserAsync` caches user existence for 24 h in `IMemoryCache` — subsequent updates from the same user skip the DB round-trip entirely.

Direct writes to `users.coins` via raw SQL are **forbidden** and bypassed entirely by `EconomicsService`'s `FOR UPDATE` path. The `admin_audit` table records every admin-triggered balance mutation.

Events logged per call: `economics.credit / economics.debit / economics.debit_rejected / economics.adjust_unchecked`.

## Migrations

Per-module, Dapper-based, tracked in `__module_migrations` under a `module_id` key. Applied at startup by `ModuleMigrationRunner` (registered before `BotHostedService` so schema is ready before polling starts).

Adding a migration = one new `Migration("name", "SQL")` entry in the module's `IModuleMigrations.GetMigrations()` list. No EF tooling.

Framework migrations (`_framework` module):

| ID | Contents |
|---|---|
| `001_event_store` | `module_events` table + indexes |
| `002_snapshots` | `module_snapshots` table |
| `003_users` | `users` table |
| `004_event_log` | `event_log` table |
| `005_admin_audit` | `admin_audit` table |

## Poker (DDD split)

```
games/Games.Poker/
├── Domain/          — pure hand-level logic, no DB / no I/O
│   ├── Deck.cs              (BuildShuffled, Draw, Parse)
│   ├── HandEvaluator.cs     (7-card → HandRank via C(7,5)=21 enumeration)
│   ├── PokerAction.cs       (PokerActionKind, AutoAction)
│   ├── Transitions.cs       (ValidationResult, TransitionKind, Transition, ShowdownEntry)
│   └── PokerDomain.cs       (StartHand, Validate, Apply, ResolveAfterAction, DecideAutoAction)
├── Application/
│   ├── PokerResults.cs      (PokerError, TableSnapshot, typed result records)
│   └── PokerService.cs      (Dapper stores + Gate + domain calls + EconomicsService)
└── Presentation/
    ├── PokerCommand.cs          (discriminated union)
    ├── PokerCommandParser.cs    (text + callback data → PokerCommand)
    └── PokerStateRenderer.cs    (table + showdown → HTML)
```

`PokerTurnTimeoutJob` (registered as `IBackgroundJob`) polls every 10 s for stuck hands and runs `DecideAutoAction` (check if possible, else fold). Same path handles restart recovery.

## Secret Hitler (DDD split)

```
games/Games.SecretHitler/
├── Domain/
│   ├── ShPolicyDeck.cs       (17 cards: 6L + 11F, serialized as "LFFL…" strings; auto-reshuffle)
│   ├── ShRoleDealer.cs       (player count → role distribution; uses RandomNumberGenerator)
│   └── ShTransitions.cs      (full state machine: StartGame, Nomination, Vote, PresidentDiscard,
│                               ChancellorEnact, FailElection, AdvancePresident)
├── Application/
│   ├── ShResults.cs          (ShError, ShGameSnapshot, typed result records)
│   └── SecretHitlerService.cs (Dapper + Gate + EconomicsService for buy-ins/refunds)
└── Presentation/
    ├── ShCommand.cs, ShCommandParser.cs, ShStateRenderer.cs
```

`SecretHitlerGateCleanupJob` (registered as `IBackgroundJob`) prunes idle gates every 10 min.

## Horse racing

Lighter than poker/SH — no domain layer, per-race not per-turn.

- `HorseService` — bets, admin-gated race execution, payout math, participant DMs
- GIF renderer: SkiaSharp canvas frames → LZW GIF89a, offloaded to `Task.Run` to free the async scheduler
- `HorseGifCache` — in-memory cache of today's race GIF for `/horse result`

Race only runs with ≥ `MinBetsToRun` bets (default 4). Race date is `MM-dd-yyyy` in UTC+7.

## Admin web UI

Razor pages under `/admin/*` served on the same port as the webhook (3000).

### Authentication

`GET /admin/login` — two sign-in methods:

1. **Telegram Login Widget** — HMAC-SHA256 with `SHA256(botToken)` as key, 24 h freshness check. Requires `Bot:Username` and bot domain registered in BotFather. Works on real domains only, not localhost.
2. **Token form** — constant-time comparison against `Bot:AdminWebToken`. Works everywhere. Used for local dev.

Successful auth writes an `AdminSession` (userId, name, role) to `ISession`. Gate middleware redirects unauthenticated `/admin/*` requests to `/admin/login`.

### RBAC

| Role | Source config | Permissions |
|---|---|---|
| `SuperAdmin` | `Bot:Admins` | Full access — can mutate balances, run races |
| `ReadOnly` | `Bot:ReadOnlyAdmins` | View only — mutation endpoints return 403 |

### Audit log

Every write action from the admin UI is recorded in `admin_audit`:

| Column | Description |
|---|---|
| `actor_id` / `actor_name` | Telegram user from session |
| `action` | e.g. `users.set_coins`, `horse.run_race` |
| `details` | JSONB with action-specific context |
| `occurred_at` | TIMESTAMPTZ |

### Pages

- `/admin` — dashboard: stats, user search
- `/admin/users` — user list, balance set/adjust (SuperAdmin only, via `IEconomicsService.AdjustUncheckedAsync`)
- `/admin/horse` — race control panel: today's bets, koefs, "Run race" button (SuperAdmin only)
- `/admin/bets` — pending bets
- `/admin/history` — race history
- `/admin/events` — event log

## Configuration

`appsettings.json` is the source of truth; env vars override (Docker: `environment` block + `.env` file; K8s: Secret-backed env).

### `Bot` section

| Key | Required | Description |
|---|---|---|
| `Token` | yes | Telegram bot API token |
| `Username` | yes | Bot @username (with or without @) |
| `Admins` | yes | List of Telegram user IDs with SuperAdmin access |
| `ReadOnlyAdmins` | no | List of Telegram user IDs with ReadOnly access |
| `AdminWebToken` | no | Token for password-based admin login |
| `IsProduction` | no | `true` → webhook; `false` → polling (default) |
| `WebhookPort` | no | Kestrel port in webhook mode (default 3000) |
| `TrustedChannel` | no | @username for race GIF broadcast |
| `StartingCoins` | no | Coins for new users (default 100) |

### Other sections

| Section | Key | Required when |
|---|---|---|
| `ConnectionStrings` | `Postgres` | always |
| `Redis` | `Enabled`, `ConnectionString` | `Redis:Enabled=true` |
| `ClickHouse` | `Enabled`, `Host` | `ClickHouse:Enabled=true` |

## Running

```bash
# Local dev (polling mode, needs local Postgres)
dotnet build
dotnet run --project host/CasinoShiz.Host

# Tests
dotnet test

# Full stack (Postgres + Redis + ClickHouse + Grafana)
cp .env.example .env   # fill Bot__Token, Bot__Username, Bot__Admins__0
docker compose up --build
```

### Ports & URLs (docker-compose defaults)

| Service | Port | URL | Purpose |
|---|---|---|---|
| Bot (ASP.NET) | 3000 | `http://localhost:3000` | Webhook + admin UI + health |
| Admin UI | 3000 | `http://localhost:3000/admin/login` | Razor pages, session-gated |
| Health (live) | 3000 | `http://localhost:3000/health/live` | Liveness probe |
| Health (ready) | 3000 | `http://localhost:3000/health/ready` | Readiness probe |
| Postgres | 5432 | `postgres://cazino:cazino@localhost:5432/cazino` | Primary datastore |
| Redis | 6379 | `redis://localhost:6379` | Streams + CAP transport |
| ClickHouse HTTP | 8123 | `http://localhost:8123` | Analytics queries |
| Grafana | 3001 | `http://localhost:3001` | Dashboards (admin/admin) |

### Helm (Kubernetes)

The `helm/` chart deploys the bot, a Postgres StatefulSet, and a Redis StatefulSet. ClickHouse and Grafana are not included — analytics disabled by default.

**1. Build and push image:**

```bash
docker build -t your-registry/casinoshiz:latest .
docker push your-registry/casinoshiz:latest
```

**2. Create `helm/values.secret.yaml`** (git-ignored, never commit):

```yaml
bot:
  token: "YOUR_TELEGRAM_BOT_TOKEN"
  username: "@YourBotUsername"
  admins:
    - 123456789   # your Telegram numeric user ID

postgres:
  password: "strong-password-here"

redis:
  enabled: true
  password: ""   # set if you want Redis auth
```

**3. Install:**

```bash
helm install casinoshiz helm/ \
  -f helm/values.yaml \
  -f helm/values.secret.yaml \
  --namespace casinoshiz --create-namespace
```

**4. Upgrade after changes:**

```bash
helm upgrade casinoshiz helm/ \
  -f helm/values.yaml \
  -f helm/values.secret.yaml \
  --namespace casinoshiz
```

**Webhook (production).** Set `bot.isProduction: true` and enable ingress:

```yaml
bot:
  isProduction: true

ingress:
  enabled: true
  className: nginx
  host: bot.example.com
  tls:
    enabled: true
    secretName: casinoshiz-tls
```

Register webhook with Telegram after deploy:

```
https://api.telegram.org/bot<TOKEN>/setWebhook?url=https://bot.example.com/<TOKEN>
```

## Analytics

`ClickHouseAnalyticsService` (singleton) buffers events and flushes every `FlushIntervalMs`. Creates its table on startup. If ClickHouse is unreachable at startup the service logs and disables itself — the bot never blocks on analytics.

Validation: if `ClickHouse:Enabled=true` but `ClickHouse:Host` is empty, startup throws immediately.

Raw query: `curl 'http://localhost:8123/?query=SELECT+*+FROM+analytics.events_v2+LIMIT+10'`

## Testing

631 xUnit tests under `tests/CasinoShiz.Tests/`. No external dependencies — all DB-touching tests use in-memory fakes (`FakeEconomicsService`, `InMemoryBlackjackHandStore`, etc.).

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~HandEvaluatorTests"
dotnet test --filter "DisplayName~majorityJa"
```

Coverage: domain logic (poker, secret hitler, blackjack, dice), services, taxes, PRNG, Russian plurals, router attribute scanning, InProcessEventBus, all framework contracts.

## Database schema

All persistence is PostgreSQL + Dapper. No EF Core in the live codebase.

### Framework tables

#### `users`

| Column | Type | Notes |
|---|---|---|
| `telegram_user_id` | BIGINT | PRIMARY KEY |
| `display_name` | TEXT NOT NULL | |
| `coins` | INTEGER NOT NULL DEFAULT 0 | written only by `EconomicsService` |
| `version` | BIGINT NOT NULL DEFAULT 0 | bumped on every balance mutation |
| `created_at` | TIMESTAMPTZ | |
| `updated_at` | TIMESTAMPTZ | |

#### `admin_audit`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGSERIAL | PRIMARY KEY |
| `actor_id` | BIGINT NOT NULL | Telegram user ID of admin |
| `actor_name` | TEXT NOT NULL | |
| `action` | TEXT NOT NULL | e.g. `users.set_coins` |
| `details` | JSONB NOT NULL DEFAULT '{}' | action-specific context |
| `occurred_at` | TIMESTAMPTZ | |

#### `module_events` / `module_snapshots` / `event_log`

Event-sourcing stack for future aggregate use. See `FrameworkMigrations.cs` for full DDL.

---

### Game tables

#### `dicecube_bets` / `darts_bets` / `basketball_bets` / `bowling_bets`

Pending bet state for two-step dice games. Identical shape:

| Column | Type |
|---|---|
| `user_id` | BIGINT (PK) |
| `chat_id` | BIGINT (PK) |
| `amount` | INTEGER |
| `created_at` | TIMESTAMPTZ |

#### `blackjack_hands`

| Column | Type | Notes |
|---|---|---|
| `user_id` | BIGINT | PRIMARY KEY |
| `chat_id` | BIGINT | |
| `bet` | INTEGER | |
| `player_cards` | TEXT | space-separated card codes |
| `dealer_cards` | TEXT | |
| `deck_state` | TEXT | |
| `state_message_id` | INTEGER NULL | private DM edited in place |
| `created_at` | TIMESTAMPTZ | |

#### `poker_tables` / `poker_seats`

See `games/Games.Poker/` migrations for full DDL. Key columns: `invite_code` (PK), `status`, `phase`, `deck_state`, `last_action_at`. Seats keyed on `(invite_code, position)`.

#### `secret_hitler_games` / `secret_hitler_players`

See `games/Games.SecretHitler/` migrations. Deck serialized as `L`/`F` strings. Key columns: `status`, `phase`, `deck_state`, `discard_state`, `election_tracker`, `last_action_at`.

#### `horse_bets` / `horse_results`

Day-scoped, keyed on `race_date` (`MM-dd-yyyy` UTC+7). Results hold the winner horse index + GIF bytes.

#### `redeem_codes`

| Column | Type | Notes |
|---|---|---|
| `code` | UUID | PRIMARY KEY |
| `active` | BOOLEAN | false once redeemed |
| `issued_by` | BIGINT | |
| `redeemed_by` | BIGINT NULL | |

## Bot commands

All UI in Russian. Command names are ASCII.

### Everyone

| Command | Effect |
|---|---|
| `🎰` | Spin the slot machine — deducts `DiceCost`, prize table payout |
| `/dice bet <amount>` + `🎲` | Place stake, roll cube. 4→x2, 5→x3, 6→x5 |
| `/darts bet <amount>` + `🎯` | Place stake, throw. 4→x2, 5→x3, 6→x6 |
| `/horse bet <1-N> <amount>` | Bet on today's race |
| `/horse info` | Today's bets + koefs |
| `/horse result` | Today's winner GIF |
| `/poker …` | Texas Hold'em — create / join / start / fold / call / raise / check / leave |
| `/blackjack <bet>` | Start a hand; inline keyboard drives hit / stand / double |
| `/sh …` | Secret Hitler (5–10 players) — create / join / start / nominate / vote / leave |
| `/redeem <uuid>` | Redeem freespin code (private chat only, emoji captcha) |
| `/balance` | Current coin balance |
| `/top` | Per-chat leaderboard |
| `/help` | Command reference |

### Bot-admin-only (`Bot:Admins`)

| Command | Effect |
|---|---|
| `/horserun` | Run today's race, render GIF, pay winners |
| `/codegen [count]` | Generate freespin codes |
| `/run pay <id> <amount>` | Manual coin adjustment |
| `/run userinfo` | Reply to message → Telegram user ID |
| `/run cancel_blackjack <id>` | Refund and remove stuck hand |
| `/run kick_poker <id>` | Remove from table and refund stack |
| `/rename <old> <new\|*>` | Display-name override; `*` clears |

## Conventions

- User strings: Russian, live in each module's `Locales.cs` — never inline
- Plural forms: `RussianPlural.Plural(n, ["монета","монеты","монет"])`
- PRNG: `Mulberry32` seeded for reproducible outcomes (captcha, horse speeds, poker shuffle)
- No `Task.Delay` for scheduling — use `IBackgroundJob` / `IHostedService` sweepers
- Services return result records; handlers map to messages. Only throw for programmer errors
- **Balance changes go through `IEconomicsService` — always.** Never raw SQL on `users.coins`
- Logging: source-generated `[LoggerMessage]` only. No string interpolation in log calls
- Primary constructors are the default style: `public sealed class Foo(Dep dep) : IBar`
- Services: `Scoped`; hosted services / analytics: `Singleton`; `ITelegramBotClient`: `Singleton`
