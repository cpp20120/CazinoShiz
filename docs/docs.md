# CasinoShiz

A Telegram casino/gambling mini-game bot. Russian-language UI. Games: slots (🎰), dice cube (🎲), darts (🎯), horse racing, Texas Hold'em poker, blackjack, **Secret Hitler (🗳)**, freespin code redemption.

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 (preview SDK) |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | **PostgreSQL 16** via EF Core 10 (CRUD + migrations) + **Dapper** (balance hot path with `SELECT ... FOR UPDATE`) |
| Analytics | ClickHouse 24.x via `ClickHouse.Client` 7.x (buffered, degrades gracefully) |
| Dashboards | Grafana 11 with auto-provisioned ClickHouse datasource |
| Graphics | SkiaSharp 3.x (horse race GIF renderer) |
| Tests | xUnit, EF Core InMemory, 128 tests covering services + domain + router |
| Deploy | Docker Compose (bot + postgres + clickhouse + grafana) |

UTC+7 is used for "day" resets (`Helpers/TimeHelper.cs`).

## Layout

Three-project solution (`CasinoShiz.slnx` at repo root):

```
CasinoShiz/
├── docker-compose.yml                — bot + postgres + clickhouse + grafana
├── Dockerfile                        — dotnet/sdk:10.0-preview
├── CasinoShiz.slnx                   — solution manifest
├── data/                             — volumes (postgres, clickhouse)
├── grafana/                          — datasource + dashboards provisioning
├── docs/docs.md                      — this document
├── README.md
├── src/
│   ├── CasinoShiz/                   — ASP.NET Core Web host
│   │   ├── Program.cs                — DI composition root + webhook + admin middleware
│   │   ├── appsettings.json          — config (token, admins, game tuning, conn string)
│   │   ├── Configuration/            — POCO options (BotOptions, ClickHouseOptions)
│   │   └── Pages/Admin/              — Razor pages for /admin UI
│   ├── CasinoShiz.Core/              — all business logic; root namespace `CasinoShiz` (flat)
│   │   ├── Generators/               — SkiaSharp race frames, LZW GIF89a encoder
│   │   ├── Helpers/                  — Mulberry32 PRNG, Locales, TimeHelper, …
│   │   └── Services/
│   │       ├── BotHostedService.cs             — IHostedService: polling / webhook, auto-migrates DB
│   │       ├── UpdateHandler.cs                — entrypoint: wraps Update into ctx
│   │       ├── PokerTurnTimeoutService.cs      — hosted sweeper for stuck poker turns
│   │       ├── BlackjackHandTimeoutService.cs  — hosted sweeper for stuck blackjack hands
│   │       ├── CaptchaService.cs, TaxService.cs
│   │       ├── Analytics/ClickHouseReporter.cs
│   │       ├── Pipeline/                       — middleware, router, route attributes
│   │       ├── Handlers/                       — Telegram transport, one per feature
│   │       ├── Economics/                      — balance bounded context (Dapper + FOR UPDATE)
│   │       ├── Admin/, Dice/, Horse/, Blackjack/, Leaderboard/, Redeem/
│   │       ├── Poker/{Domain,Application,Presentation}
│   │       └── SecretHitler/{Domain,Application,Presentation}
│   └── CasinoShiz.Data/              — EF Core DbContext + entities + migrations
│       ├── Data/AppDbContext.cs
│       ├── Data/Entities/            — POCOs (UserState, PokerTable, BlackjackHand, …)
│       └── Migrations/               — EF migrations + ModelSnapshot
└── tests/CasinoShiz.Tests/           — xUnit project (128 tests)
```

Namespaces are flat under `CasinoShiz.*` regardless of project/folder depth (e.g. `CasinoShiz.Services.Economics`, not `CasinoShiz.Core.Services.Economics`). `RootNamespace` is set explicitly in `CasinoShiz.Core.csproj`.

## Architecture

### Request flow

```
Telegram ─► BotHostedService (polling loop  OR  webhook POST /{token})
         └─► UpdateHandler.HandleUpdateAsync
              └─► UpdatePipeline.InvokeAsync (delegate chain)
                   ├─ ExceptionMiddleware       catch + log + report to ClickHouse
                   ├─ LoggingMiddleware         scope: update_id/user_id/chat_id, duration
                   ├─ RateLimitMiddleware       per-user/per-chat rate limit
                   └─ UpdateRouter.DispatchAsync
                        first-match against attribute-scanned routes
                        └─► IUpdateHandler.HandleAsync   (DiceHandler, PokerHandler, …)
                             └─► feature service         (DiceService, PokerService, …)
                                  ├─► AppDbContext (EF) for CRUD + models
                                  ├─► EconomicsService (Dapper + FOR UPDATE) for balance
                                  └─► ClickHouseReporter
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
2. `CallbackPrefix("poker:")` and `CallbackPrefix("bj:")` (200) outrank `CallbackFallback` (1), so poker/blackjack callbacks land in their handlers and anything else falls through to `RedeemHandler`'s captcha.

To add a command: drop a handler class in `Services/Handlers/` implementing `IUpdateHandler`, decorate it with one or more route attributes, register it in `Program.cs` as scoped. No router changes needed.

### Middleware

- **`ExceptionMiddleware`** — catches everything except `OperationCanceledException` during shutdown. Logs `update.error`, reports an `error_handler` event to ClickHouse with exception type + message + stack. Swallows the exception so the polling loop keeps running.
- **`LoggingMiddleware`** — `BeginScope` with structured props (`update_id`, `user_id`, `chat_id`, `kind`). Logs `update.in` at entry and `update.out` at exit with `duration_ms` measured via `Stopwatch.GetTimestamp`. Text is truncated.
- **`RateLimitMiddleware`** — per-user / per-chat token-bucket limiter; drops updates above the threshold to keep handlers from being hammered.

All logging uses source-generated `[LoggerMessage]` for zero-allocation structured logs.

### Handler vs Service

Handlers in `Services/Handlers/` are the transport layer. They own:

- parsing text commands (or delegating to a parser),
- mapping service-level error enums (`PokerError`, `HorseError`, `DiceOutcome`, `BlackjackError`, `BeginRedeemError`, …) to localized Russian strings,
- rendering state (inline keyboards, Markdown/HTML messages),
- calling the corresponding Service.

Services own domain logic + DB + ClickHouse + logs. They return plain result records (`DicePlayResult`, `PayResult`, `BlackjackResult`, …) — never throw for business-rule violations. This keeps handlers trivial and makes services reusable from non-Telegram code (e.g. `PokerTurnTimeoutService` calls `PokerService.RunAutoActionAsync` directly; `BlackjackHandTimeoutService` settles stuck blackjack hands).



## Economics (balance bounded context)

`Services/Economics/EconomicsService.cs` is the **only** place balances mutate. Every service that wants to change a user's `Coins` goes through it:

```csharp
await economics.DebitAsync(user, amount, "horse.bet", ct);           // throws on insufficient funds
await economics.TryDebitAsync(user, amount, "poker.join", ct);       // bool: did it succeed?
await economics.CreditAsync(user, payout, "blackjack.settle", ct);
await economics.AdjustAsync(user, delta, "dice.play", ct);           // positive or negative
await economics.AdjustUncheckedAsync(user, delta, "admin.pay", ct);  // allows negative balance (admin only)
```

Internally:

- On a relational provider (Postgres in prod), each call opens `SELECT "Coins", "Version" FROM "Users" WHERE "TelegramUserId" = @id FOR UPDATE`, validates the change, writes back via Dapper, and bumps `Version`. The query uses EF's `DbConnection` so if the caller has started a `DbContext` transaction the mutation joins that transaction — balance + bet insert land atomically.
- On the `InMemoryDatabase` provider (unit tests), it falls back to direct mutation of the tracked entity since there's no real DB to lock.

Because Dapper is the sole writer on UPDATE, EF is configured to ignore `Coins` and `Version` on save (`AppDbContext.OnModelCreating` sets `SetAfterSaveBehavior(Ignore)` on both). EF still writes them on INSERT for new users. A typo of `user.Coins += 10` anywhere outside `EconomicsService` is now a silent no-op at persistence time — easy to catch in review, impossible to half-commit.

`Version` is monotonically incremented per mutation for audit + future replication use; the FOR UPDATE lock replaces the old optimistic-concurrency token.

Events logged per call: `economics.credit / economics.debit / economics.debit_rejected / economics.adjust_unchecked` with `{UserId, Amount, Balance, Reason}`.

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
│   └── PokerService.cs      (EF access + SemaphoreSlim Gate + emits domain calls + EconomicsService for buy-ins/cashout)
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

**Concurrency.** A single `PokerService.Gate` (`SemaphoreSlim(1,1)`) serializes all write operations across all tables. For the expected load (a handful of concurrent tables) this is fine. If it ever matters, move to a per-table gate keyed on `InviteCode`. Balance changes (buy-in, cashout) go through `EconomicsService` so they participate in the same row-level lock as any other balance-touching operation.

**Timeouts.** `PokerTurnTimeoutService` (hosted) polls every 10 s for `PokerTable` rows where `Status == HandActive && LastActionAt < now - PokerTurnTimeoutMs`. For each stuck table it calls `PokerService.RunAutoActionAsync(code)` — which runs `PokerDomain.DecideAutoAction` (check if possible, else fold) and broadcasts. Same path handles restart recovery: after the bot restarts, the sweeper picks up any hand still waiting on a player.

**UI model.** Each seated player has a private DM with one `StateMessageId` that the bot edits in place on every state change (`editMessageText`, falls back to a fresh send if deleted). Hole cards live in that DM naturally — the bot stores only the last message ID, not the rendered text.

## Blackjack

`Services/Blackjack/BlackjackService.cs` runs classic single-deck blackjack against the dealer. Public API:

- `StartAsync(userId, displayName, chatId, bet, ct)` — debits bet via EconomicsService, deals two cards each, auto-settles on natural blackjack.
- `HitAsync(userId, ct)` — adds a card; auto-settles on bust.
- `StandAsync(userId, ct)` — dealer draws to 17, then resolves.
- `DoubleAsync(userId, ct)` — doubles the bet, draws exactly one card, resolves.
- `GetSnapshotAsync(userId, ct)` / `SetStateMessageIdAsync(...)` — UI helpers for in-place edits (same `StateMessageId` pattern as poker).

Hand state lives in a `BlackjackHands` table keyed on `UserId` (one active hand per user). Dealer rule: hit until hard 17+. Payouts: push = bet back, win = 2× bet, natural blackjack = 2.5× bet.

`BlackjackHandTimeoutService` is a hosted sweeper that force-settles hands idle past a threshold so no bet is stuck indefinitely.

## Secret Hitler (🗳)

A 5–10 player hidden-role social deduction game. Players pool a buy-in, roles are dealt (Liberal / Fascist / Hitler), and each round the rotating President nominates a Chancellor, the full table votes Ja/Nein, and the elected government enacts one of three drawn policies. Winning condition: Liberals enact 5 liberal policies or execute Hitler; Fascists enact 6 fascist policies or get Hitler elected Chancellor with ≥3 fascist policies on the board.

Lives under `Services/SecretHitler/` with the same DDD split as poker:

```
Services/SecretHitler/
├── Domain/         — pure rules, no DB / no Telegram
│   ├── ShPolicyDeck.cs       (17-card deck: 6 Liberal + 11 Fascist, serialized as "LFFL…" strings;
│   │                          Draw, AddToDiscard, PeekTop; auto-reshuffle discard into deck when
│   │                          deck < drawCount)
│   ├── ShRoleDealer.cs       (player count → (liberals, fascists) + 1 Hitler; shuffled via
│   │                          RandomNumberGenerator)
│   └── ShTransitions.cs      (StartGame, Validate/Apply Nomination / Vote / PresidentDiscard /
│                              ChancellorEnact; FailElection handles the 3-failed-election forced
│                              policy with automatic win check; AdvancePresident skips dead seats)
├── Application/
│   ├── ShResults.cs          (ShError, ShGameSnapshot, typed *Result records)
│   └── SecretHitlerService.cs (EF access + SemaphoreSlim Gate + EconomicsService for buy-ins /
│                               refunds + ClickHouse events)
└── Presentation/
    ├── ShCommand.cs          (discriminated union: Create / Join / Start / Leave / Nominate / Vote /
    │                          PresidentDiscard / ChancellorEnact / NominateMenu / …)
    ├── ShCommandParser.cs    (text + callback → ShCommand)
    └── ShStateRenderer.cs    (game state → HTML for each private DM)
```

**State machine.** `ShStatus` is `Lobby → Active → Completed | Closed`. `ShPhase` within `Active` is `Nomination → Election → LegislativePresident → LegislativeChancellor → …` cycling each round, terminating in `GameEnd`. `ShTransitions` is the single source of truth for legal transitions and returns typed results (`ShAfterVoteResult`, `ShAfterEnactResult`) so the application layer can route to side effects (credit pot, edit DMs, log events) without re-deriving state.

**Deck serialization.** `DeckState` / `DiscardState` / `PresidentDraw` / `ChancellorReceived` are all plain `L`/`F` strings — no JSON, no separate card table. `ShPolicyDeck.Draw(ref deck, ref discard, n)` mutates both strings in place and handles the reshuffle-on-exhaustion case transparently.

**Term limits.** The last elected President + Chancellor can't be re-nominated as Chancellor next round (6+ players), only the last Chancellor for 5-player games — encoded in `ValidateNomination`.

**UI model.** Same private-DM + `StateMessageId` pattern as poker and blackjack: each player has one DM message that gets edited in place on every state change. Roles are revealed once in DM after `StartGame` deals them; subsequent edits never re-reveal.

**Concurrency.** One `SemaphoreSlim` gates all writes across all rooms — identical to poker. Buy-in refunds on cancel/leave go through `EconomicsService` so they participate in the balance row lock.

## Horse racing

`Services/Horse/` mirrors the poker split but lighter (no domain layer — the game is per-race, not per-turn):

- `HorseService` — bets, admin-gated `/horserun` + web-admin `RunRaceFromAdminAsync`, payout math, emits `horse_bet` + `horse_run` events.
- `HorseHandler` — thin transport, maps `HorseError` to Russian strings.
- `Generators/HorseRaceRenderer` + `SpeedGenerator` + `GifEncoder` — SkiaSharp canvas frames stitched into an LZW GIF89a sent to chats as a GIF document.

`HorseResult` and `HorseBet` are keyed on `RaceDate` (string, MM-dd-yyyy in UTC+7) so everything is day-scoped.

**Race gate.** A race only runs with at least `HorseService.MinBetsToRun = 4` bets for the day — `RunRaceCoreAsync` short-circuits with `HorseError.NotEnoughBets` otherwise. `RunRaceAsync(callerId, …)` adds an admin check on top; `RunRaceFromAdminAsync(…)` is used by the web panel where the caller is already authenticated by the `Bot:AdminWebToken` gate.

**Notifications.** `RaceOutcome.Participants` is a per-user summary `(UserId, TotalBet, Payout)` built from the full bet list before deletion. `AdminService.RunHorseRaceAsync` iterates it after the race and DMs each bettor the race GIF (as `SendAnimation`) with a localized caption — winners see net profit, losers see their lost stake. Per-user `try/catch` swallows `403 Forbidden` from users who never started a private chat with the bot. The GIF is also broadcast to `Bot:TrustedChannel` when configured.

## Dice cube (🎲)

`Services/Dice/DiceCubeService.cs` + `DiceCubeHandler`. Two-step interaction per chat:

1. `/dice bet <amount>` — debits the stake via `EconomicsService.DebitAsync` and inserts a pending `DiceCubeBet` row keyed on `(UserId, ChatId)`. Only one pending bet per (user, chat) at a time.
2. User rolls the native 🎲 emoji — the handler reads `message.Dice.Value` (1–6), multiplies the stake by `Multipliers[face]`, credits the payout, and deletes the pending bet.

Multipliers: `1/2/3 → x0`, `4 → x2`, `5 → x3`, `6 → x5`. Emits `dicecube_bet` and `dicecube_roll` events.

## Darts (🎯)

`Services/Dice/DartsService.cs` + `DartsHandler` — structurally identical to dice cube but against its own `DartsBets` table (so a user can hold one cube bet and one darts bet in the same chat simultaneously).

Multipliers: `1/2/3 → x0`, `4 → x2`, `5 → x3`, `6 (bullseye) → x6`. Emits `darts_bet` and `darts_throw` events.

## Dice

`DiceService` + `DiceHandler`. Telegram encodes the slot machine outcome (🎰) as an integer 1–64 where bits `[0:1]`, `[2:3]`, `[4:5]` select each of the three reels. The service handles:

- daily attempt limit (default 3, extendable via freespin codes → `ExtraAttempts`)
- gas tax on the stake (`TaxService.GetGasTax` — 2.85% × √2, or a log curve for small stakes)
- bank tax on idle balances compounding per inactive day (`TaxService.GetBankTax`)
- prize tables for normal play and redeem mode (when `AttemptCount >= AttemptsLimit` but `ExtraAttempts > 0`)
- probabilistic freespin code drops (`FreecodeProbability`) in group chats

Returns `DicePlayResult { Outcome, Prize, Loss, NewBalance, TotalAttempts, MoreRolls, Tax, DaysWithoutRolls, FreespinCode? }`. `DiceOutcome` covers `Forwarded / AttemptsLimit / NotEnoughCoins / Played`.

## Redeem (freespin codes)

`RedeemService` + `RedeemHandler` + `CaptchaService`. Codes generated by an admin (`/codegen`, in-group) and redeemed by users (`/redeem <uuid>` in private chat). The captcha is emoji-based: `CaptchaService` picks N random items from a fixed Russian emoji list using `Mulberry32` seeded by the code, corrupts ~25% of characters in descriptions via a typo map, and asks the user to match. In-memory state `RedeemHandler.PendingCaptchas` expires after 15 s. Callback-based UI — this is why the router has a `[CallbackFallback]` route to `RedeemHandler`.

Successful redemption adds `ExtraAttempts` (default +3) to the user and sets `FreespinCode.Active = false`.

## Leaderboard & balance

`LeaderboardService` returns grouped places (same-coin users share a place) via `GetTopAsync(chatId, limit, ct)` and a `BalanceInfo` (with a `visible` flag for hiding long-inactive users) via `GetBalanceAsync`. `LeaderboardHandler` serves `/top`, `/balance`, `/help`, `/__debug`.

## Admin

### Telegram commands

`AdminService` handles: `usersync` (syncs the user table to ClickHouse for analytics joins), `userinfo` (reply-to → user id), `pay <id> <amount>` (manual coin adjustment via `EconomicsService.AdjustUncheckedAsync`), `getUser <id>` (raw JSON dump), `rename <old> <new|*>` (display-name override, `*` clears). Also: `cancel_blackjack` (refund an active blackjack hand), `kick_poker` (remove a user from their poker table, refund stack), and the web-only `CancelDiceCubeBetAsync` / `CancelDartsBetAsync` / `ResetSlotAttemptsAsync` actions exposed as POST endpoints on the user detail page. All Telegram admin commands gate on `BotOptions.Admins` containing the caller's Telegram ID.

Events emitted: `admin_command` with `{ command, calleeId, … }` and `user_map` during `usersync`.

#### Becoming an admin

1. Get your numeric Telegram user ID (message `@userinfobot`).
2. Add it to `Bot:Admins` in `src/CasinoShiz/appsettings.json`:
   ```json
   "Admins": [123456789]
   ```
3. Restart the bot — options are bound at startup.

Telegram admin-only commands: `/horserun`, `/run <subcmd>`, `/codegen`, `/rename`, `/renames`, `/notification`. Non-admin callers of `/horserun` are silently ignored.

### Admin web UI

The ASP.NET app serves Razor pages under `/admin` (see `src/CasinoShiz/Pages/`) on the same port as the webhook (`3000`). Runtime compilation is enabled, so `.cshtml` edits don't require a rebuild. Pages:

- `/admin` — overview stats (users, poker tables, blackjack hands, pending horse/cube/darts bets, active freespin codes, **Secret Hitler lobbies + active rooms + players + pot locked**) + user search with htmx-driven live filtering.
- `/admin/user/{id}` — user detail: balance, slot attempts, recent horse bets, issued freespin codes, active poker seat, active blackjack hand, pending cube/darts bets, **active Secret Hitler membership (room, seat, role, status/phase)**. Each card exposes the relevant admin action (pay, rename, reset attempts, cancel bet, kick from poker, refund blackjack).
- `/admin/horse` — horse race control panel. Shows today's bets, per-horse stakes and koefs, and a "Run race" button enabled only when `BetsCount >= MinBetsToRun`. On run, renders the GIF inline, broadcasts it to `TrustedChannel`, and DMs each bettor the result.
- `/admin/sh` — Secret Hitler rooms list. Shows every non-closed room: invite code, host, status/phase, player count, buy-in, pot, last-action time.
- `/admin/sh/{code}` — room detail: full game state (policy counts, election tracker, current president + nominated chancellor), seat list with roles and last votes, and a "Cancel room & refund all players" danger-zone action. Cancellation iterates `Players`, credits each stack back via `EconomicsService`, marks the game `Closed`, and emits a `cancel_sh_room` event.

Access is gated by a shared-secret token:

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

`ClickHouseReporter` is a singleton that buffers events (size 10, interval 3 s, `Timer`-driven flush). Events are tagged with `EventType` (e.g. `dice`, `horse_bet`, `poker_action`, `blackjack`, `admin_command`, `error_handler`, `update_in`) and a `Payload` object serialized via `System.Text.Json`. If ClickHouse is unreachable at startup the connection is set to null and events become no-ops — the bot never blocks on analytics.

Table schema is ensured once at startup (`CreateTableIfNotExists`). The table is wide and schemaless-ish: a `timestamp` column plus an `event_type` and a JSON `payload` column; downstream queries unpack the JSON.

### Event catalog

Every game emits events — no feature skips analytics. `EventType` strings are stable and used in Grafana dashboards.

| Source | `EventType` values |
|---|---|
| Slots 🎰 (`DiceService`) | `dice` (forwarded / no_coins / attempts_limit / played), `codegen` (probabilistic freespin drop) |
| Dice cube 🎲 (`DiceCubeService`) | `dicecube_bet`, `dicecube_roll` |
| Darts 🎯 (`DartsService`) | `darts_bet`, `darts_throw` |
| Horse (`HorseService`) | `horse_bet`, `horse_run` |
| Poker (`PokerService`) | `poker_create`, `poker_join`, `poker_hand_start`, `poker_action`, `poker_auto`, `poker_leave`, `poker_hand_end` |
| Blackjack (`BlackjackService`) | `blackjack` (start / hit / stand / double / settle) |
| Secret Hitler (`SecretHitlerService`) | `sh_create`, `sh_join`, `sh_start`, `sh_nominate`, `sh_vote`, `sh_president_discard`, `sh_chancellor_enact`, `sh_leave` |
| Redeem (`RedeemService`) | `codegen`, `redeem` (start / success / captcha_fail / expired / already_used) |
| Leaderboard (`LeaderboardService`) | `achievement` (top-1 users on `/top`) |
| Admin (`AdminService`) | `admin_command` (covers `pay`, `rename`, `kick_poker`, `cancel_blackjack`, `cancel_sh_room`, dice-cube/darts cancels, attempts reset), `user_map` (one row per user during `usersync`) |
| Chats (`ChatHandler`, `ChannelHandler`) | `regchat`, `forward_channel_post`, `leave_channel` |
| Pipeline (`ExceptionMiddleware`, `LoggingMiddleware`) | `error_handler`, `update_in` |

**Not in ClickHouse:** `EconomicsService` balance mutations (`economics.credit/debit/adjust_unchecked/debit_rejected`) are structured **logs only**, not events. Money flow is already derivable by joining the per-game events above. Similarly, `update.in/out` duration timing lives in logs via `LoggingMiddleware` — only exceptions get reported to ClickHouse as `error_handler`.

## Data model

All entities are plain POCOs under `CasinoShiz.Data/Data/Entities/`, each using `[MaxLength]` data annotations so EF emits correct Postgres column sizes.

| Entity | Key | Indexes | Purpose |
|---|---|---|---|
| `UserState` | `TelegramUserId` | — | Coins, daily attempts, last-seen day (UTC+7), Version counter |
| `ChatRegistration` | `ChatId` | — | Chats that receive channel broadcasts + game events |
| `HorseBet` | `Id` (Guid) | `(RaceDate, UserId)` | Day-scoped bets; race winner resolves all |
| `HorseResult` | `RaceDate` | — | One row per day; holds the winner + last-frame PNG |
| `FreespinCode` | `Code` (Guid) | `Active` | Code lifecycle: issued → redeemed (`Active=false`) |
| `DisplayNameOverride` | `OriginalName` | — | Admin-set rename; keyed on *old* display name |
| `PokerTable` | `InviteCode` (8 chars) | `Status` | Per-table state machine + deck |
| `PokerSeat` | `(InviteCode, Position)` composite | `UserId` | One row per seated player |
| `BlackjackHand` | `UserId` | — | At most one active hand per user |
| `DiceCubeBet` | `(UserId, ChatId)` composite | — | Pending 🎲 stake awaiting a roll |
| `DartsBet` | `(UserId, ChatId)` composite | — | Pending 🎯 stake awaiting a throw |
| `SecretHitlerGame` | `InviteCode` (8 chars) | `Status` | Secret Hitler room: status/phase, policy counts, deck + discard strings, election tracker, winner |
| `SecretHitlerPlayer` | `(InviteCode, Position)` composite | `UserId` | One row per seated SH player — role, alive, last vote, DM `StateMessageId` |

`UserState.Coins` and `UserState.Version` are configured with `SetAfterSaveBehavior(PropertySaveBehavior.Ignore)` — EF writes them on INSERT only. Post-insert, `EconomicsService` (Dapper) owns those columns.

### Migrations

`CasinoShiz.Data/Migrations/20260420000000_InitialCreate.cs` is the consolidated Postgres baseline. Subsequent migrations add table by table:

- `20260420000001_AddDiceCubeBets` — `DiceCubeBets` table for 🎲 pending bets.
- `20260421000000_AddDartsBets` — `DartsBets` table for 🎯 pending bets.
- `20260422000000_AddSecretHitler` — `SecretHitlerGames` + `SecretHitlerPlayers` tables.

`ModelSnapshotBuilder.cs` is shared between every migration's `.Designer.cs` and `AppDbContextModelSnapshot.cs` — add new entities there as well as in the migration. `BotHostedService` runs `db.Database.MigrateAsync(cancellationToken)` on startup — new deployments get the schema; existing ones skip migrations already recorded in `__EFMigrationsHistory`.

Generate subsequent migrations from `src/CasinoShiz` (the `Microsoft.EntityFrameworkCore.Design` tooling lives on the web project):

```bash
dotnet ef migrations add <Name> --project ../CasinoShiz.Data --startup-project .
dotnet ef database update --project ../CasinoShiz.Data --startup-project .
```

## Testing

`tests/CasinoShiz.Tests/` — xUnit project, 128 tests. Uses `DisableTransitiveFrameworkReferences=true` so the test host runs on plain .NET Core without requiring ASP.NET Core runtime (the web project is Web SDK; the transitive reference would otherwise demand it).

Covered:

- **`DicePrizeTests`** — `DecodeRolls` bit-packing, `GetMoreRollsAvailable` across current-day / new-day / exhausted / extras.
- **`DiceServiceTests`** — `PlayAsync` with in-memory EF: forwarded, new user, not-enough-coins, attempts-exhausted, redeem mode, success path.
- **`BlackjackServiceTests`** — start/hit/stand/double paths, natural blackjack, push, dealer hit-to-17, bust settlement.
- **`BlackjackHandValueTests`** — ace-as-1 vs ace-as-11, soft totals, natural detection.
- **`AdminServiceTests`** — cancel blackjack refund path.
- **`HandEvaluatorTests`** — royal flush beats quads, full house beats flush, wheel straight, category mapping.
- **`HorseServiceTests`** — bet placement, insufficient-bets gate, payout math.
- **`TaxServiceTests`** — gas tax (zero / large), bank tax (low / mid / high / cap).
- **`Mulberry32Tests`** — deterministic sequences, different seeds, range invariants.
- **`RussianPluralTests`** — nominative / genitive / plural forms across edge cases.
- **`UpdateRouterTests`** — every handler implements `IUpdateHandler`, attribute presence per handler, `/horserun` priority > `/horse`.
- **`ShPolicyDeckTests`** — Serialize/Parse roundtrip, Draw from top, auto-reshuffle discard into deck when deck < drawCount, `AddToDiscard`, `PeekTop` (incl. empty-deck fallback), full-deck composition (6L+11F).
- **`ShRoleDealerTests`** — role distribution for every supported player count (5→3/1, 6→4/1, 7→4/2, 8→5/2, 9→5/3, 10→6/3 plus 1 Hitler), out-of-range throws, `DealRoles` assigns by position.
- **`ShTransitionsTests`** — full domain state machine: nomination validation (wrong phase / not president / self / dead target / term limits for 5 vs ≥6 players), `ApplyNomination` advances phase and clears votes, vote validation (already-voted / dead), `ApplyVote` pending vs pass vs tie-fails, Hitler-elected win with ≥3 fascist policies, 3rd-failed-election forced policy + liberal win via forced enact, president discard index bounds + discard flow, chancellor enact liberal/fascist win thresholds (5 / 6), `AdvancePresident` skips dead seats and wraps around, `StartGame` resets all state and picks lowest-alive position as first president.

Run: `dotnet test` (or `dotnet test --filter "FullyQualifiedName~BlackjackServiceTests"` for a single class).

Tests run against EF Core's `InMemoryDatabase`. `EconomicsService` detects this via `db.Database.IsRelational()` and falls back to direct entity mutation — Dapper + FOR UPDATE only engages against Postgres. Testcontainers-postgres is a planned upgrade so the locking path is exercised under test.

## Configuration

`src/CasinoShiz/appsettings.json` is the source of truth; `appsettings.Development.json` and Docker `.env` override. Secrets `Bot:Token` and `Bot:Admins` must be set to run the bot. `ConnectionStrings:Postgres` points at the Postgres instance.

`Bot` section highlights:

- `IsProduction` — `true` switches from long polling to webhook (`POST /{token}`).
- `TrustedChannel` — channel whose posts `ChannelHandler` forwards to registered chats (default `@cazinonews`).
- `Poker*` — buy-in, blinds, max players (≤6), turn timeout (ms).
- `Blackjack{Min,Max}Bet` — per-hand bet bounds.
- `FreecodeProbability`, `AttemptsLimit`, `DiceCost` — dice tuning.
- `DaysOfInactivityToHideInTop` — leaderboard visibility cutoff.
- `CaptchaItems` — how many emoji items to present in a redeem captcha.
- `AdminWebToken` — shared secret gating `/admin` pages.

`ClickHouse` section: set `Enabled: false` to silence analytics locally. If enabled but unreachable, the reporter logs and drops events instead of blocking the request.

## Running

```bash
cd CasinoShiz
dotnet build
dotnet run --project src/CasinoShiz 
dotnet test                
```

Docker (brings up everything the bot needs):

```bash
docker compose up --build              # postgres + clickhouse + bot + grafana
```

The compose file overrides `ConnectionStrings__Postgres` on the bot service so it resolves `Host=postgres` (the compose service name) rather than `localhost`. Postgres and ClickHouse have healthchecks; the bot waits for `service_healthy` on both.

`/health` endpoint returns `ok` in any mode.

### Ports & URLs

Defaults, from `docker-compose.yml`:

| Service | Host port | URL | Purpose |
|---|---|---|---|
| Bot (ASP.NET) | 3000 | `http://localhost:3000` | Webhook + admin UI + `/health` |
| Bot — webhook | 3000 | `POST http://localhost:3000/{botToken}` | Telegram pushes updates here when `IsProduction=true` |
| Bot — admin UI | 3000 | `http://localhost:3000/admin?token=…` | Razor pages, token-gated |
| Postgres | 5432 | `postgres://cazino:cazino@localhost:5432/cazino` | Primary datastore |
| ClickHouse HTTP | 8123 | `http://localhost:8123` | Analytics queries (`?query=SELECT…`) |
| ClickHouse native | 9000 | — | Native protocol |
| Grafana | 3001 | `http://localhost:3001` | Dashboards (default `admin`/`admin`) |

### Analytics

- **Grafana** — http://localhost:3001, credentials from `.env` (`GRAFANA_ADMIN_USER`/`GRAFANA_ADMIN_PASSWORD`, default `admin`/`admin`). The ClickHouse datasource is auto-provisioned from `grafana/provisioning/`.
- **Raw ClickHouse** — `curl 'http://localhost:8123/?query=SELECT+*+FROM+analytics.events+LIMIT+10'` or `docker compose exec clickhouse clickhouse-client`.

## Bot commands

All UI is in Russian; command names are ASCII.

### Everyone

| Command | Effect |
|---|---|
| `🎰` (native dice emoji) | Spin the slot machine — deducts `DiceCost` from balance, applies gas + bank tax, pays from prize table. |
| `/dice bet <amount>` + `🎲` | Place a stake, then roll the cube. 4→x2, 5→x3, 6→x5. One pending bet per (user, chat). |
| `/darts bet <amount>` + `🎯` | Place a stake, then throw. 4→x2, 5→x3, 6 (bullseye)→x6. Independent from cube. |
| `/balance` | Current coins + tier emoji. |
| `/top` | Per-chat leaderboard; inactive users hidden after `DaysOfInactivityToHideInTop`. |
| `/help` | Russian command reference. |
| `/horse bet <1-N> <amount>` | Place a bet on today's race. |
| `/horse info` | Current day's bets + koefs. |
| `/horse result` | Today's winner image (if a race has run). |
| `/redeem <uuid>` | Redeem a freespin code (**private chat only**, emoji captcha). |
| `/poker …` | See `PokerCommandParser.cs` — create / join / start / fold / call / raise / check / leave. |
| `/blackjack <bet>` | Start a blackjack hand; callback buttons drive hit / stand / double. |
| `/sh …` | Secret Hitler (5–10 players). See `ShCommandParser.cs` — create / join `<code>` / start / leave / nominate / vote. All gameplay after start runs in private DMs via inline keyboards. |

### Chat-owner-only (gated via Telegram `getChatMember`)

| Command | Effect |
|---|---|
| `/regchat` | Register the current chat to receive channel broadcasts + game events. |
| `/notification <text>` | Broadcast to all registered chats. |

### Bot-admin-only (caller's Telegram ID must be in `Bot:Admins`)

| Command | Effect |
|---|---|
| `/horserun` | Runs today's race, renders GIF, pays winners. Requires ≥ `HorseService.MinBetsToRun` bets (default 4). Silent no-op for non-admins. |
| `/codegen [count]` | Generate freespin code(s) in a group. |
| `/run usersync` | Sync user table → ClickHouse. |
| `/run userinfo` | Reply to a message → returns that user's ID. |
| `/run pay <id> <amount>` | Manual coin adjustment (via `EconomicsService.AdjustUncheckedAsync`). |
| `/run getUser <id>` | JSON dump of a `UserState`. |
| `/run cancel_blackjack <id>` | Refund and remove a stuck blackjack hand. |
| `/run kick_poker <id>` | Remove a user from their poker table and refund their stack. |
| `/rename <old> <new\|*>` | Display-name override; `*` clears it. |
| `/renames` | List all active overrides. |

New users start with **100 coins** and **3 daily attempts**. Day rolls over at midnight UTC+7 (`TimeHelper.GetRaceDate`).

### Telegram menu button

On startup, `BotHostedService` calls `SetMyCommands` with a curated list that shows up in Telegram's blue menu button next to the chat input: `/dice`, `/darts`, `/blackjack`, `/poker`, `/sh`, `/horse`, `/redeem`, `/top`, `/balance`, `/help`. Edit the list in `BotHostedService.cs` and restart — Telegram picks up the new menu automatically.

## Conventions

- User-facing strings are Russian and live in `Helpers/Locales.cs`. Don't inline literals in handlers — add a formatter method.
- Plural forms via `RussianPlural.Plural(n, ["монета","монеты","монет"])` — picks the right of three variants based on Russian grammar rules.
- Seeded RNG: `Mulberry32` — used anywhere the outcome must be reproducible (captcha, horse speeds, poker shuffle).
- No `Task.Delay` for scheduling — use an `IHostedService` sweeper so state survives restart (see `PokerTurnTimeoutService`, `BlackjackHandTimeoutService`).
- Services return result records; handlers map them to messages. Only throw for programmer errors, not user input.
- EF Core is for CRUD + models + migrations. Dapper is for hot paths where raw SQL matters — today, only `EconomicsService`; leaderboard and horse-bet queries are next on the list.
- **Balance changes go through `EconomicsService` — always.** Never mutate `UserState.Coins` directly. Direct writes to `Coins` are silently dropped by EF (by design) so the compiler won't catch the mistake, only review will.
- Logging: source-generated `[LoggerMessage]` only. Each event gets a stable name. No string interpolation in log calls.
- Primary constructors are the default (e.g. `public sealed class Foo(Dep dep) : IBar`).
- Services are `Scoped`; hosted services and `ClickHouseReporter` are `Singleton`; `ITelegramBotClient` and `NpgsqlDataSource` are `Singleton`.
- Adding a route: decorate the handler class with an attribute from `RouteAttributes.cs`. No central registration.
