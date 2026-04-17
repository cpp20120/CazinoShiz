# CasinoShiz

A Telegram casino/gambling mini-game bot. Russian-language UI. Games: dice casino (🎰), horse racing, Texas Hold'em poker, freespin code redemption.

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | SQLite via EF Core 10 with migrations (`Database.MigrateAsync` on startup) |
| Analytics | ClickHouse via `ClickHouse.Client` 7.x (buffered, degrades gracefully) |
| Graphics | SkiaSharp 3.x (horse race GIF renderer) |
| Tests | xUnit, EF Core InMemory, 47 tests covering services + domain + router |
| Deploy | Docker Compose (bot + clickhouse) |

UTC+7 is used for "day" resets (`Helpers/TimeHelper.cs`).

## Layout

```
BusinoBot/                           (repo folder — project name is CasinoShiz)
├── docker-compose.yml                — bot + clickhouse services
├── Dockerfile
├── BusinoBot.slnx                    — solution manifest (src + tests)
├── data/                             — mounted into container (busino.db, CH state)
├── docs/docs.md                      — this document
├── src/CasinoShiz/
│   ├── Program.cs                    — DI composition root + webhook endpoint
│   ├── appsettings.json              — config (token, admins, game tuning)
│   ├── Configuration/                — POCO options (BotOptions, ClickHouseOptions)
│   ├── Data/
│   │   ├── AppDbContext.cs           — EF Core DbContext + OnModelCreating
│   │   └── Entities/                 — POCOs (UserState, PokerTable, HorseBet, …)
│   ├── Migrations/                   — EF migrations + ModelSnapshot
│   ├── Generators/                   — SkiaSharp race frames, LZW GIF89a encoder
│   ├── Helpers/                      — Mulberry32 PRNG, Locales, TimeHelper, …
│   └── Services/
│       ├── BotHostedService.cs           — IHostedService: polling loop / webhook
│       ├── UpdateHandler.cs              — thin entrypoint: wraps Update into ctx
│       ├── PokerTurnTimeoutService.cs    — IHostedService: 10s sweeper for stuck turns
│       ├── CaptchaService.cs, TaxService.cs
│       ├── Analytics/ClickHouseReporter.cs
│       ├── Pipeline/                     — middleware, router, attributes
│       ├── Handlers/                     — Telegram transport, one per feature
│       ├── Admin/     AdminService      + Results
│       ├── Dice/      DiceService       + Results
│       ├── Horse/     HorseService      + Results
│       ├── Leaderboard/ LeaderboardService + Results
│       ├── Redeem/    RedeemService     + Results
│       └── Poker/     Domain / Application / Presentation
└── tests/BusinoBot.Tests/              — xUnit project
```

## Architecture

### Request flow

```
Telegram ─► BotHostedService (polling loop  OR  webhook POST /{token})
         └─► UpdateHandler.HandleUpdateAsync
              └─► UpdatePipeline.InvokeAsync (delegate chain)
                   ├─ ExceptionMiddleware       catch + log + report to ClickHouse
                   ├─ LoggingMiddleware         scope: update_id/user_id/chat_id, duration
                   └─ UpdateRouter.DispatchAsync
                        first-match against attribute-scanned routes
                        └─► IUpdateHandler.HandleAsync   (DiceHandler, PokerHandler, …)
                             └─► feature service         (DiceService, PokerService, …)
                                  └─► AppDbContext (EF) + ClickHouseReporter
```

The pipeline is composed in `Services/Pipeline/UpdatePipeline.cs` as a plain delegate chain. Adding middleware = one file + one line. Order is explicit in `UpdatePipeline.cs` — exception first so it wraps everything downstream.

### UpdateContext

`Services/Pipeline/UpdateContext.cs` gives a uniform view over the three update kinds we actually handle (`Message` / `CallbackQuery` / `ChannelPost`). Handlers read `ctx.UserId`, `ctx.ChatId`, `ctx.Text`, `ctx.CallbackData` without re-checking which `Update` variant came in. `ctx.Services` is the request-scoped `IServiceProvider`; `ctx.Ct` is the per-update cancellation token.

### Router (attribute-based)

Routes are declared on each handler class, not in a central table. `UpdateRouter` scans the assembly once at startup for classes implementing `IUpdateHandler` and reads their `RouteAttribute`s into a priority-sorted list.

```csharp
[Command("/horse")]
[Command("/horserun")]
public sealed class HorseHandler : IUpdateHandler { … }
```

Attribute family (`Services/Pipeline/RouteAttributes.cs`):

| Attribute | Matches | Priority |
|---|---|---|
| `[ChannelPost]` | `Update.ChannelPost != null` | 300 |
| `[MessageDice(emoji)]` | native Telegram dice with given emoji | 250 |
| `[CallbackPrefix(prefix)]` | `CallbackQuery.Data` starts with prefix | 200 |
| `[Command(prefix)]` | `Message.Text` starts with prefix | 100 + prefix.Length |
| `[CallbackFallback]` | any remaining `CallbackQuery` | 1 |

Two ordering rules fall out of this scheme for free:

1. `/horserun` outranks `/horse` because its prefix is longer (priority 109 vs 106).
2. `CallbackPrefix("poker:")` (200) outranks `CallbackFallback` (1), so poker callbacks land in `PokerHandler` and anything else falls through to `RedeemHandler`'s captcha.

To add a command: drop a handler class in `Services/Handlers/` implementing `IUpdateHandler`, decorate it with one or more route attributes, register it in `Program.cs` as scoped. No router changes needed.

### Middleware

- **`ExceptionMiddleware`** — catches everything except `OperationCanceledException` during shutdown. Logs `update.error` (EventId 1900), reports an `error_handler` event to ClickHouse with exception type + message + stack. Swallows the exception so the polling loop keeps running.
- **`LoggingMiddleware`** — `BeginScope` with structured props (`update_id`, `user_id`, `chat_id`, `kind`). Logs `update.in` (1001) at entry and `update.out` (1002) at exit with `duration_ms` measured via `Stopwatch.GetTimestamp`. Text is truncated to 80 chars.

All logging uses source-generated `[LoggerMessage]` for zero-allocation structured logs. EventId ranges: 1000s = pipeline, 1100s = router, 1900 = error.

### Handler vs Service

Handlers in `Services/Handlers/` are the transport layer. They own:

- parsing text commands (or delegating to a parser),
- mapping service-level error enums (`PokerError`, `HorseError`, `DiceOutcome`, `BeginRedeemError`, …) to localized Russian strings,
- rendering state (inline keyboards, Markdown/HTML messages),
- calling the corresponding Service.

Services own domain logic + DB + ClickHouse + logs. They return plain result records (`DicePlayResult`, `PayResult`, `BeginRedeemResult`, …) — never throw for business-rule violations. This keeps handlers trivial and makes services reusable from non-Telegram code (e.g. `PokerTurnTimeoutService` calls `PokerService.RunAutoActionAsync` directly).

The split is applied to **every** feature now: Dice, Redeem, Leaderboard, Admin, Horse, Poker each have a matching `<Feature>Service` + `<Feature>Results`. Channel and Chat are still transport-only because they have minimal logic.

Handler line counts as of this doc:

| Handler | LOC |
|---|---|
| PokerHandler | 296 |
| RedeemHandler | 195 |
| ChatHandler | 148 |
| HorseHandler | 145 |
| AdminHandler | 143 |
| LeaderboardHandler | 115 |
| DiceHandler | 93 |
| ChannelHandler | 78 |

## Poker (DDD split)

Poker is the most complex feature and lives under `Services/Poker/` with three layers:

```
Services/Poker/
├── Domain/          — pure hand-level logic, no DB / no ILogger / no ClickHouse
│   ├── Deck.cs              (BuildShuffled, Draw, Parse — 52-card deck ops)
│   ├── HandEvaluator.cs     (7-card → HandRank via C(7,5)=21 enumeration)
│   ├── PokerAction.cs       (PokerActionKind, PokerAction record, AutoAction)
│   ├── Transitions.cs       (ValidationResult, TransitionKind, Transition, ShowdownEntry)
│   └── PokerDomain.cs       (StartHand, Validate, Apply, ResolveAfterAction, DecideAutoAction)
├── Application/     — orchestration: transactions, logs, ClickHouse
│   ├── PokerResults.cs      (PokerError, TableSnapshot, Create/Join/Leave/Start/ActionResult)
│   └── PokerService.cs      (EF access + SemaphoreSlim Gate + emits domain calls)
└── Presentation/    — Telegram surface
    ├── PokerCommand.cs          (discriminated union: Usage/Create/Join/Start/Raise/…)
    ├── PokerCommandParser.cs    (text + callback data → PokerCommand)
    └── PokerStateRenderer.cs    (table + showdown → HTML)
```

The domain layer operates on `PokerTable` / `PokerSeat` entities (mutates in place) but knows nothing about EF, Telegram, or buy-ins. A future tournament engine could replay hands through the same `PokerDomain` without touching invite-code/chat logic.

**Turn model.** `PokerDomain.ResolveAfterAction` consumes the current betting-round state and returns a `Transition { Kind, FromPhase, ToPhase, Showdown? }`. Five kinds:

- `TurnAdvanced` — next seat to act
- `PhaseAdvanced` — flop → turn, turn → river, etc.
- `HandEndedLastStanding` — everyone else folded
- `HandEndedRunout` — all-ins with no more betting; remaining streets dealt out
- `HandEndedShowdown` — reached river; hands compared

`PokerService.ResolveAfterActionAsync` is the thin shim that invokes the domain, persists, and translates the transition into `ActionResult` + structured logs + ClickHouse events (`poker_hand_end` with reason).

**Concurrency.** A single `PokerService.Gate` (`SemaphoreSlim(1,1)`) serializes all write operations across all tables. For the expected load (a handful of concurrent tables) this is fine. If it ever matters, move to a per-table gate keyed on `InviteCode`.

**Timeouts.** `PokerTurnTimeoutService` (hosted) polls every 10 s for `PokerTable` rows where `Status == HandActive && LastActionAt < now - PokerTurnTimeoutMs`. For each stuck table it calls `PokerService.RunAutoActionAsync(code)` — which runs `PokerDomain.DecideAutoAction` (check if possible, else fold) and broadcasts. Same path handles restart recovery: after the bot restarts, the sweeper picks up any hand still waiting on a player.

**UI model.** Each seated player has a private DM with one `StateMessageId` that the bot edits in place on every state change (`editMessageText`, falls back to a fresh send if deleted). Hole cards live in that DM naturally — the bot stores only the last message ID, not the rendered text.

## Horse racing

`Services/Horse/` mirrors the poker split but lighter (no domain layer — the game is per-race, not per-turn):

- `HorseService` — bets, admin-gated `/horserun`, payout math, emits `horse_bet` + `horse_run` events.
- `HorseHandler` — thin transport, maps `HorseError` to Russian strings.
- `Generators/HorseRaceRenderer` + `SpeedGenerator` + `GifEncoder` — SkiaSharp canvas frames stitched into an LZW GIF89a sent to chats as a GIF document.

`HorseResult` and `HorseBet` are keyed on `RaceDate` (string, MM-dd-yyyy in UTC+7) so everything is day-scoped.

## Dice

`DiceService` + `DiceHandler`. Telegram encodes the slot machine outcome (🎰) as an integer 1–64 where bits `[0:1]`, `[2:3]`, `[4:5]` select each of the three reels. The service handles:

- daily attempt limit (default 3, extendable via freespin codes → `ExtraAttempts`)
- gas tax on the stake (`TaxService.GetGasTax` — 2.85% × √2, or a log curve for small stakes)
- bank tax on idle balances compounding per inactive day (`TaxService.GetBankTax`)
- prize tables for normal play and redeem mode (when `AttemptCount >= AttemptsLimit` but `ExtraAttempts > 0`)
- probabilistic freespin code drops (`FreecodeProbability`) in group chats

Returns `DicePlayResult { Outcome, Prize, Loss, NewBalance, TotalAttempts, MoreRolls, Tax, DaysWithoutRolls, FreespinCode? }`. `DiceOutcome` covers `Forwarded / AttemptsLimit / NotEnoughCoins / Played`.

## Redeem (freespin codes)

`RedeemService` + `RedeemHandler` + `CaptchaService`. Codes generated by an admin (`/codegen`, in-group) and redeemed by users (`/redeem <uuid>` in private chat). The captcha is emoji-based: `CaptchaService` picks N random items from a fixed Russian emoji list using `Mulberry32` seeded by the code, corrupts ~25% of characters in descriptions via a typo map, and asks the user to match. In-memory state `RedeemHandler.PendingCaptchas` expires after 15 s via `Task.Run(async () => await Task.Delay(15_000); remove())`. Callback-based UI — this is why the router has a `[CallbackFallback]` route to `RedeemHandler`.

Successful redemption adds `ExtraAttempts` (default +3) to the user and sets `FreespinCode.Active = false`.

## Leaderboard & balance

`LeaderboardService` returns grouped places (same-coin users share a place) via `GetTopAsync(chatId, limit, ct)` and a `BalanceInfo` (with a `visible` flag for hiding long-inactive users) via `GetBalanceAsync`. `LeaderboardHandler` serves `/top`, `/balance`, `/help`, `/__debug`.

## Admin

`AdminService` handles: `usersync` (syncs the user table to ClickHouse for analytics joins), `userinfo` (reply-to → user id), `pay <id> <amount>` (manual coin adjustment), `getUser <id>` (raw JSON dump), `rename <old> <new|*>` (display-name override, `*` clears). All admin commands gate on `BotOptions.Admins` containing the caller's Telegram ID.

Events emitted: `admin_command` with `{ command, calleeId, … }` and `user_map` during `usersync`.

### Becoming an admin (Telegram)

1. Get your numeric Telegram user ID (message `@userinfobot`).
2. Add it to `Bot:Admins` in `src/CasinoShiz/appsettings.json`:
   ```json
   "Admins": [123456789]
   ```
3. Restart the bot — options are bound at startup.

Telegram admin-only commands: `/horserun`, `/run <subcmd>`, `/codegen`, `/rename`, `/renames`, `/notification`. Non-admin callers of `/horserun` are silently ignored (see `HorseHandler.HandleRunAsync`).

### Admin web UI

The ASP.NET app serves Razor pages under `/admin` (see `src/CasinoShiz/Pages/`) on the same port as the webhook (`3000`). Access is gated by a shared-secret query-string token:

1. Set `Bot:AdminWebToken` in `appsettings.json` to an unguessable string (e.g. `openssl rand -hex 32`):
   ```json
   "AdminWebToken": "9f2c…"
   ```
2. Restart the bot.
3. Open `http://localhost:3000/admin?token=9f2c…` — the middleware stores an `admin_token` cookie (HttpOnly, SameSite=Strict, 30 days), so subsequent `/admin` visits work without the query param.

Responses if misconfigured:

| State | HTTP | Body |
|---|---|---|
| `AdminWebToken` not set | 503 | `Admin UI disabled: Bot:AdminWebToken not set` |
| Wrong / missing token | 401 | `Unauthorized` |

Leave `AdminWebToken` empty in any environment where `/admin` should stay disabled — the gate fails closed.

## Analytics

`ClickHouseReporter` is a singleton that buffers events (size 10, interval 3 s, `Timer`-driven flush). Events are tagged with `EventType` (e.g. `dice`, `horse_bet`, `poker_action`, `admin_command`, `error_handler`, `update_in`) and a `Payload` object serialized via `System.Text.Json`. If ClickHouse is unreachable at startup the connection is set to null and events become no-ops — the bot never blocks on analytics.

Table schema is ensured once at startup (`CreateTableIfNotExists`). The table is wide and schemaless-ish: a `timestamp` column plus an `event_type` and a JSON `payload` column; downstream queries unpack the JSON.

## Data model

All entities are plain POCOs under `Data/Entities/`, each using `[MaxLength]` data annotations for string columns so EF emits correct `VARCHAR(n)` in migrations (SQLite itself doesn't enforce it).

| Entity | Key | Indexes | Purpose |
|---|---|---|---|
| `UserState` | `TelegramUserId` | — | Coins, daily attempts, last-seen day (UTC+7) |
| `ChatRegistration` | `ChatId` | — | Chats that receive channel broadcasts + game events |
| `HorseBet` | `Id` (Guid) | `(RaceDate, UserId)` | Day-scoped bets; race winner resolves all |
| `HorseResult` | `RaceDate` | — | One row per day; holds the winner + GIF bytes |
| `FreespinCode` | `Code` (Guid) | `Active` | Code lifecycle: issued → redeemed (`Active=false`) |
| `DisplayNameOverride` | `OriginalName` | — | Admin-set rename; keyed on *old* display name |
| `PokerTable` | `InviteCode` (8 chars) | `Status` | Per-table state machine + deck |
| `PokerSeat` | `(InviteCode, Position)` composite | `UserId` | One row per seated player |

### Migrations

`Migrations/20260418000000_InitialCreate.cs` is the baseline migration matching the current model. `BotHostedService` runs `db.Database.MigrateAsync(cancellationToken)` on startup — new deployments get the schema; existing ones skip if the migration history row is already present.

**Caveat:** if you have an existing `busino.db` created by the old `EnsureCreated` path (no `__EFMigrationsHistory` table), `MigrateAsync` will try to create tables that already exist and fail. Fix options: (a) drop the DB and recreate, or (b) insert a row into `__EFMigrationsHistory` with `MigrationId = '20260418000000_InitialCreate'` to mark it applied. New installs are unaffected.

Subsequent migrations should be generated with `dotnet ef migrations add <Name>` (requires the matching ASP.NET Core 10 runtime to be installed locally). The baseline `InitialCreate` and its `AppDbContextModelSnapshot` were hand-authored because the tooling wasn't available in the dev environment at the time.

## Testing

`tests/BusinoBot.Tests/BusinoBot.Tests.csproj` — xUnit project, 47 tests. Uses `DisableTransitiveFrameworkReferences=true` so the test host runs on plain .NET Core without requiring ASP.NET Core runtime (the main project is Web SDK; the transitive reference would otherwise demand it).

Covered:

- **`DicePrizeTests`** — `DecodeRolls` bit-packing, `GetMoreRollsAvailable` across current-day/new-day/exhausted/extras.
- **`DiceServiceTests`** — `PlayAsync` with in-memory EF: forwarded, new user, not-enough-coins, attempts-exhausted, redeem mode, success path.
- **`TaxServiceTests`** — gas tax (zero / large), bank tax (low / mid / high / cap).
- **`Mulberry32Tests`** — deterministic sequences, different seeds, range invariants.
- **`RussianPluralTests`** — nominative/genitive/plural forms across edge cases.
- **`HandEvaluatorTests`** — royal flush beats quads, full house beats flush, wheel straight, category mapping.
- **`UpdateRouterTests`** — every handler implements `IUpdateHandler`, attribute presence per handler, `/horserun` priority > `/horse`.

Run: `dotnet test tests/BusinoBot.Tests/BusinoBot.Tests.csproj`.

## Configuration

`src/CasinoShiz/appsettings.json` is the source of truth; `appsettings.Development.json` and Docker `.env` override. Secrets `Bot:Token` and `Bot:Admins` must be set to run the bot.

`Bot` section highlights:

- `IsProduction` — `true` switches from long polling to webhook (`POST /{token}`).
- `TrustedChannel` — channel whose posts `ChannelHandler` forwards to registered chats (default `@businonews`).
- `Poker*` — buy-in, blinds, max players (≤6), turn timeout (ms).
- `FreecodeProbability`, `AttemptsLimit`, `DiceCost` — dice tuning.
- `DaysOfInactivityToHideInTop` — leaderboard visibility cutoff.
- `CaptchaItems` — how many emoji items to present in a redeem captcha.

`ClickHouse` section: set `Enabled: false` to silence analytics locally. If enabled but unreachable, the reporter logs and drops events instead of blocking the request.

## Running

```bash
cd BusinoBot
dotnet build
dotnet run --project src/CasinoShiz   # polling mode
dotnet test                            # 47 tests
```

Docker:

```bash
cd BusinoBot
docker compose up --build              # brings up bot + clickhouse + grafana
```

The ClickHouse healthcheck uses `clickhouse-client --query 'SELECT 1'` (Alpine BusyBox `wget` doesn't handle the ping endpoint reliably).

`/health` endpoint returns `ok` in any mode and is the Docker healthcheck target for the bot service.

### Ports & URLs

Defaults, from `docker-compose.yml`:

| Service | Host port | URL | Purpose |
|---|---|---|---|
| Bot (ASP.NET) | 3000 | `http://localhost:3000` | Webhook + admin UI + `/health` |
| Bot — webhook | 3000 | `POST http://localhost:3000/{botToken}` | Telegram pushes updates here when `IsProduction=true` |
| Bot — admin UI | 3000 | `http://localhost:3000/admin?token=…` | Razor pages, token-gated |
| Bot — health | 3000 | `http://localhost:3000/health` | Returns `ok` |
| ClickHouse HTTP | 8123 | `http://localhost:8123` | Queries (`?query=SELECT…`) |
| Grafana | 3001 | `http://localhost:3001` | Dashboards (default `admin`/`admin`) |

`Bot:WebhookPort` in `appsettings.json` sets the in-container listen port; the host mapping comes from `docker-compose.yml`. When running the bot locally (`dotnet run`) against Docker ClickHouse, the app's `ClickHouse:Host` stays as `http://localhost:8123`; when the bot runs inside compose, point it at `http://clickhouse:8123` via `.env`.

### Analytics

- **Grafana** — http://localhost:3001, credentials from `.env` (`GRAFANA_ADMIN_USER`/`GRAFANA_ADMIN_PASSWORD`, default `admin`/`admin`). The ClickHouse datasource is auto-provisioned; the `overview.json` dashboard in `grafana/dashboards/` loads on first boot.
- **Raw ClickHouse** — `curl 'http://localhost:8123/?query=SELECT+*+FROM+analytics.events+LIMIT+10'` or `docker compose exec clickhouse clickhouse-client`.

## Bot commands

All UI is in Russian; command names are ASCII.

### Everyone

| Command | Effect |
|---|---|
| `🎰` (native dice emoji) | Spin the slot machine — deducts `DiceCost` from balance, applies gas + bank tax, pays from prize table. |
| `/balance` | Current coins + tier emoji (`NameDecorators`). |
| `/top` | Per-chat leaderboard; inactive users hidden after `DaysOfInactivityToHideInTop`. |
| `/help` | Russian command reference. |
| `/horse bet <1-N> <amount>` | Place a bet on today's race. |
| `/horse info` | Current day's bets + koefs. |
| `/horse result` | Today's winner image (if a race has run). |
| `/redeem <uuid>` | Redeem a freespin code (**private chat only**, emoji captcha). |
| `/poker …` | See `PokerCommandParser.cs` — create/join/start/fold/call/raise/check/leave. |

### Chat-owner-only (gated via Telegram `getChatMember`)

| Command | Effect |
|---|---|
| `/regchat` | Register the current chat to receive channel broadcasts + game events. |
| `/notification <text>` | Broadcast to all registered chats. |

### Bot-admin-only (caller's Telegram ID must be in `Bot:Admins`)

| Command | Effect |
|---|---|
| `/horserun` | Runs today's race, renders GIF, pays winners. Silent no-op for non-admins. |
| `/codegen [count]` | Generate freespin code(s) in a group. |
| `/run usersync` | Sync user table → ClickHouse. |
| `/run userinfo` | Reply to a message → returns that user's ID. |
| `/run pay <id> <amount>` | Manual coin adjustment. |
| `/run getUser <id>` | JSON dump of a `UserState`. |
| `/rename <old> <new\|*>` | Display-name override; `*` clears it. |
| `/renames` | List all active overrides. |

New users start with **100 coins** and **3 daily attempts**. Day rolls over at midnight UTC+7 (`TimeHelper.GetRaceDate`).

## Conventions

- User-facing strings are Russian and live in `Helpers/Locales.cs`. Don't inline literals in handlers — add a formatter method.
- Plural forms via `RussianPlural.Plural(n, ["монета","монеты","монет"])` — picks the right of three variants based on Russian grammar rules.
- Seeded RNG: `Mulberry32` — used anywhere the outcome must be reproducible (captcha, horse speeds, poker shuffle).
- No `Task.Delay` for scheduling — use an `IHostedService` sweeper so state survives restart (see `PokerTurnTimeoutService`).
- Services return result records; handlers map them to messages. Only throw for programmer errors, not user input.
- Single-writer SQLite with EF Core: one `SaveChangesAsync` per logical operation is the unit of work. `PokerService` adds a process-wide `SemaphoreSlim` on top.
- Logging: source-generated `[LoggerMessage]` only. Each event gets a stable `EventId`. No string interpolation in log calls.
- Adding a route: decorate the handler class with an attribute from `RouteAttributes.cs`. No central registration.


