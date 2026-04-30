# CasinoShiz

A Telegram casino/gambling mini-game bot. Russian-language UI. Games: slots (🎰), dice cube (🎲), darts (🎯), football (⚽), bowling (🎳), basketball (🏀), horse racing, Texas Hold'em poker, blackjack, Secret Hitler (🗳), freespin code redemption, optional **peer coin transfers** (`/transfer`) in groups, **1v1 PvP challenges** (`/challenge`), and the **PixelBattle** Telegram WebApp (`/pixelbattle`).

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 (preview SDK) |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | **PostgreSQL 16** via Dapper on the live game/balance paths (balance hot path with `SELECT ... FOR UPDATE`). EF Core packages and `EfRepository<T>` exist for optional module-owned repositories. |
| Migrations | Dapper-based, tracked in `__module_migrations`, applied at startup by `ModuleMigrationRunner` |
| Event bus | DotNetCore.CAP 10.x — PostgreSQL outbox + Redis transport when `Redis:Enabled=true`; `InProcessEventBus` fallback for single-instance / dev |
| Update fan-out | Redis Streams (opt-in via `Redis:Enabled`) — partitioned by `chatId % N`, consumer groups |
| Analytics | ClickHouse 24.x via `ClickHouse.Client` 7.x (buffered, degrades gracefully) |
| Dashboards | Grafana 11 with auto-provisioned ClickHouse + Prometheus datasources |
| Graphics | SkiaSharp 3.x (horse race GIF renderer, offloaded to thread pool) |
| Tests | xUnit, 680+ tests covering domain + services + router + framework |
| Deploy | Docker Compose (bot + postgres + redis + clickhouse + prometheus + grafana) / Helm chart |

Horse race “day” and scheduled auto-run use `Games:horse:TimezoneOffsetHours` (default **7**, same convention as daily bonus / Telegram dice cap). `HorseTimeHelper.GetRaceDate(offsetHours)` builds the `MM-dd-yyyy` pool key.

## Layout

```
CasinoShiz/
├── docker-compose.yml                — bot + db/cache/analytics + monitoring stack
├── Dockerfile                        — dotnet/sdk:10.0-preview multi-stage
├── CasinoShiz.slnx                   — solution manifest
├── .env                              — local env file consumed by compose (git-ignored)
├── prometheus/                       — scrape config for exporters, cAdvisor, dotnet-monitor
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
│   └── BotFramework.Host/            — ASP.NET host, pipeline/router, economics, economics_ledger,
│                                       `IDailyBonusService`, analytics, CAP, Redis Streams, admin, migrations
├── games/
│   ├── Games.Dice/                   — slots 🎰
│   ├── Games.DiceCube/               — dice cube 🎲
│   ├── Games.Darts/                  — darts 🎯
│   ├── Games.Football/               — football ⚽
│   ├── Games.Bowling/                — bowling 🎳
│   ├── Games.Basketball/             — basketball 🏀
│   ├── Games.Blackjack/              — blackjack
│   ├── Games.Horse/                  — horse racing + GIF renderer
│   ├── Games.Poker/                  — Texas Hold'em (full DDD split)
│   ├── Games.SecretHitler/           — hidden-role social deduction (full DDD split)
│   ├── Games.Challenges/             — 1v1 PvP challenge system over existing games
│   ├── Games.PixelBattle/            — Telegram WebApp pixel canvas + SSE updates
│   ├── Games.Redeem/                 — freespin codes + emoji captcha
│   ├── Games.Leaderboard/            — /top, /balance, /daily, /help
│   ├── Games.Transfer/               — /transfer (peer coins in groups)
│   └── Games.Admin/                  — admin Telegram commands
├── host/
│   └── CasinoShiz.Host/              — composition root
│       ├── Program.cs                — AddBotFramework().AddModule<T>()…Build().UseBotFramework()
│       ├── Debug/                    — optional debug module/handlers
│       ├── appsettings.json
│       └── Pages/Admin/              — Razor pages for /admin UI
└── tests/
    └── CasinoShiz.Tests/             — 680+ xUnit tests
```

## Architecture

### Host composition

`BotFrameworkBuilder.AddBotFramework()` registers framework singletons (Telegram client, router, pipeline, middlewares, `IEconomicsService`, `IDailyBonusService`, event bus, Redis Streams, admin auth, migrations runner). Each `.AddModule<T>()` instantiates the module, runs `ConfigureServices(IModuleServiceCollection)`, and folds its handlers/locales/migrations/commands/admin pages into the shared aggregate. The current host also registers `DebugModule` before the game modules. `UseBotFramework()` wires the webhook endpoint, health endpoints, session middleware, and admin gate.

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

Wallets are scoped per **Telegram chat** (`balance_scope_id` = `Message.Chat.Id` in public groups/supergroups; in private DMs it equals the user id). The same person has separate coin balances in different chats.

`IEconomicsService` is the **only** place routine balance mutations go. All game and admin pay flows use it; every successful change appends a row to `economics_ledger` in the same transaction as `users.coins` / `version`.

```csharp
await economics.EnsureUserAsync(userId, balanceScopeId, displayName, ct);
await economics.TryDebitAsync(userId, balanceScopeId, amount, "horse.bet", ct);
await economics.CreditAsync(userId, balanceScopeId, payout, "dice.prize", ct);
await economics.AdjustUncheckedAsync(userId, balanceScopeId, delta, ct); // admin / recovery
// Atomic two-party move (used by /transfer): debit sender and credit recipient in one transaction
await economics.TryPeerTransferAsync(fromId, toId, balanceScopeId, debitTotal, creditNet, "transfer.send", "transfer.receive", ct);
```

`EnsureUserAsync` seeds new `(user, scope)` rows with `Bot:StartingCoins` and caches existence for 24 h in `IMemoryCache`.

Direct writes to `users.coins` via raw SQL are **forbidden**; `EconomicsService` uses `SELECT … FOR UPDATE` and bumps `version` on every mutation. Admin UI writes still go through `IEconomicsService` (or audit-backed paths); `admin_audit` records admin web actions.

Events logged: `economics.credit / economics.debit / economics.debit_rejected / economics.adjust_unchecked`.

### House edge (Telegram mini-games)

Mini-games that use Telegram’s random dice (🎲 🎯 🎳 🏀 ⚽) are tuned so that, if each face were **roughly uniform**, the **expected return per unit bet** is **below 100%** (long-run edge for the house). Concrete multipliers and slot stake live in `Games.Dice*`, `Games.DiceCube`, darts, bowling, basketball, football service code, or `host/.../appsettings.json` under `Games` (e.g. `Games:dice:Cost`, `Games:dicecube:Mult4`…`Mult6`).

**Rule of thumb:** for a fair \(n\)-faced die with pay multipliers \(m_1…m_n\), long-run RTP per coin staked is \(\frac{1}{n}\sum m_i\) when wins pay `stake × m_face`. Keep \(\sum m_i < n` on six-sided games (and the analogous sum on the five-faced ball games).

Slots (🎰) use a **fixed cost + gas** (`TaxService.GetGas`) and a fixed prize table over 64 encoded outcomes — tune mainly via `Games:dice:Cost` in config. Redeem-code drops are controlled per sticker game with `RedeemDropChance` (`0.02` = 2% per resolved roll/throw); a dropped code grants one extra roll for the same game that dropped it.

`DailyBonus` (see below) is a controlled **drip of coins** to players; it should stay a small % with a cap so it does not erase house edge in aggregate.

### Daily bonus (`/daily`)

`IDailyBonusService` (framework) credits a **small** once-per-local-day bonus: `floor(balance × PercentOfBalance / 100)`, capped by `MaxBonus`, ledger reason `daily.bonus`. The calendar day uses `Bot:DailyBonus:TimezoneOffsetHours` (default **7**, aligned with horse “day” in UTC+7). Column `users.last_daily_bonus_on` (migration `008`) stores the last claim.

- If the formula rounds to **0** (tiny balance) but the user is non-empty, the **day is not marked** so they can run `/daily` again after their balance moves.
- **Balance 0** → no credit, day not marked.
- If **already claimed** for that day → friendly refusal.

Config: `Bot:DailyBonus` — `Enabled`, `PercentOfBalance` (e.g. `0.35` = **0.35%** of balance, not 35%), `MaxBonus`, `TimezoneOffsetHours`. Handler: `LeaderboardHandler` on `/daily`.

### Peer transfer (`/transfer`)

Module **`Games.Transfer`**. Lets a user send coins to another user **in the same group/supergroup wallet** (`balance_scope_id` = that chat’s `Chat.Id`). **Not available in private chats with the bot** (there is no separate “recipient” context).

**Semantics**

- The **last whitespace-separated token** in the message is the amount the **recipient receives** (net).
- **Fee** is computed on that net amount from `Games:transfer` (defaults in `appsettings.json`): `FeePercent` (e.g. `0.03` = 3%), then **round to nearest 0.5**, then to a **whole coin** (`MidpointRounding.AwayFromZero`), then clamp to at least `MinFeeCoins` (default **1**). Optional `MinNetCoins`, `MaxNetCoins` (`0` = no cap).
- The sender is debited **net + fee**; the recipient is credited **net**. The fee is **not** credited anywhere (coins leave circulation). Reasons: `transfer.send` / `transfer.receive`.

**Atomicity**

- Implemented as `IEconomicsService.TryPeerTransferAsync` in **`EconomicsService`**: one DB transaction, **`SELECT … FOR UPDATE`** on **both** `(user, scope)` rows in **ascending `telegram_user_id`** order (deadlock-safe), then two ledger inserts.

**Recipient resolution** (`TransferHandler`)

Order is **explicit recipient first**, then **reply** (so `/transfer … 10` with only two tokens still works when replying to someone, but `/transfer @user 10` is not overridden by an accidental reply).

1. **`TextMention`** entity (inline user picker).
2. **`Mention`** entity (`@username`) — not the same as `TextMention`. Mentions **fully contained** in a normal `BotCommand` span (e.g. `/transfer@YourBot`) are ignored. If a client marks the **entire message** as `BotCommand`, that span is **not** used to hide mentions (otherwise every `@user` would be skipped).
3. **Whitespace-split args** (any Unicode whitespace, not only ASCII space — avoids NBSP gluing `/transfer@Bot` and `@user` into one token).
4. **Text between command and amount**: numeric Telegram user id, or `@username` / handle resolved via `getChat` (private user only). Transfers to the configured **`Bot:Username`** / `Bot:BotUsername` are rejected.
5. **Reply** to the target user’s message — only if none of the above produced a recipient.

**Config**

- Section **`Games:transfer`**: `FeePercent`, `MinFeeCoins`, `MinNetCoins`, `MaxNetCoins`.

**UX**

- Success message shows **sender balance** and **recipient balance** after the transfer. `/help` (Leaderboard module) links to `/transfer`.

### 1v1 PvP challenges (`/challenge`)

Module **`Games.Challenges`**. This is a two-player challenge layer on top of existing game primitives. It owns its own challenge table and settlement flow, but all balance movement still goes through `IEconomicsService`.

**User-facing command shapes**

```text
/challenge @username 500 dicecube
```

```text
# Reply to another user's message
/challenge 500 darts
```

The target can be resolved from a reply or from a known username in the same chat. Username lookup uses the shared `users` table scoped to the current chat (`balance_scope_id = chat_id`), so a user generally needs to have talked to the bot or played in that chat before `@username` lookup can work. Reply-based challenges do not need username lookup because Telegram gives the target user id directly.

**Supported games and aliases**

| Game enum | User aliases | Telegram / rendering path |
|---|---|---|
| `DiceCube` | `dice`, `die`, `dicecube`, `cube`, `кубик`, 🎲 | Telegram `SendDice` with 🎲 |
| `Darts` | `darts`, `dart`, `дартс`, 🎯 | Telegram `SendDice` with 🎯 |
| `Bowling` | `bowling`, `bowl`, `боулинг`, 🎳 | Telegram `SendDice` with 🎳 |
| `Basketball` | `basket`, `basketball`, `баскетбол`, 🏀 | Telegram `SendDice` with 🏀 |
| `Football` | `football`, `soccer`, `футбол`, ⚽ | Telegram `SendDice` with ⚽ |
| `Slots` | `slots`, `slot`, `casino`, `слоты`, 🎰 | Telegram `SendDice` with 🎰 |
| `Horse` | `horse`, `horses`, `race`, `лошади`, `скачки`, 🐎 | 2-horse SkiaSharp GIF |
| `Blackjack` | `blackjack`, `bj`, `21`, `блекджек`, 🃏 | auto-resolved crypto-shuffled blackjack hands |

**Lifecycle**

1. `CreateAsync` validates bet bounds (`Games:challenges:MinBet` / `MaxBet`), rejects self-challenges, checks the challenger balance, rejects duplicates between the same two users in the same chat, inserts a `Pending` row, and sends an inline keyboard.
2. Only the target user can accept or decline. Declines mark `Declined`; accepts atomically transition `Pending -> Accepted`.
3. On accept, both players are ensured in the chat wallet and debited with ledger reason `challenge.stake`. If the second debit fails, the first stake is refunded and the row becomes `Failed`.
4. The handler resolves the game. Telegram dice-style games send two Telegram dice messages and compare the returned values after a short animation delay. Horse and blackjack use specialized paths described below.
5. `CompleteAcceptedAsync` settles: equal scores refund both stakes with `challenge.tie_refund`; otherwise it pays the winner `pot - fee` with `challenge.payout`.
6. Any exception after accept calls `FailAcceptedAsync`, refunding both stakes and marking the challenge `Failed`.

**Fee math**

Challenge fee is configured as basis points:

```text
fee = clamp(HouseFeeBasisPoints, 0, 10000) * (amount * 2) / 10000
payout = (amount * 2) - fee
```

This is integer math, so tiny pots can produce a zero fee. For example, a 10-vs-10 challenge has pot `20`; at 2% the fee is `0.4`, truncated to `0`.

**Horse challenges**

Horse challenges do not use Telegram dice. The handler picks the winner with `SpeedGenerator.GenPlaces(2)`, renders a 2-horse GIF using `HorseRaceRenderer.DrawHorses`, uploads it as `challenge-horse.gif`, then waits for the GIF duration before sending the settlement message. The result scores are artificial comparison scores (`2` for winner, `1` for loser) so the shared settlement method can still choose the winner.

**Blackjack challenges**

Blackjack challenges are intentionally **instant auto-play duels**, not a turn-based blackjack table. They reuse `Games.Blackjack.Domain.Deck` and `BlackjackHandValue`:

- Build one crypto-shuffled 52-card deck.
- Draw two cards for challenger, auto-hit until total is at least 17.
- Draw two cards for target from the same deck, auto-hit until at least 17.
- Score is `0` for bust, `22` for natural blackjack, otherwise the hand total.
- Equal scores are a push and both stakes are returned.
- Cards are rendered as friendly symbols (`Q♥ K♠`, `10♦`, etc.) in the result message.

**Analytics**

The challenge service tracks `challenges.created`, `challenges.accepted`, `challenges.declined`, `challenges.tie`, `challenges.completed`, and `challenges.failed_refunded` with tags for challenge id, chat id, challenger, target, amount, game, scores, winner, payout, and fee where applicable.

## Migrations

Per-module, Dapper-based, tracked in `__module_migrations` under a `module_id` key. Applied at startup by `ModuleMigrationRunner` (registered before `BotHostedService` so schema is ready before polling starts).

Adding a migration = one new `Migration("name", "SQL")` entry in the module's `IModuleMigrations.GetMigrations()` list. No EF tooling.

Framework migrations (`_framework` module), see `FrameworkMigrations.cs`:

| ID | Contents |
|---|---|
| `001_event_store` | `module_events` + indexes |
| `002_snapshots` | `module_snapshots` |
| `003_users` | legacy single-scope `users` (superseded by 006) |
| `004_event_log` | `event_log` |
| `005_admin_audit` | `admin_audit` |
| `006_per_chat_wallets_and_ledger` | `users` with `(telegram_user_id, balance_scope_id)` PK; `economics_ledger` |
| `007_known_chats` | `known_chats` (first/last seen per chat) |
| `008_users_last_daily_bonus` | `users.last_daily_bonus_on DATE` for `/daily` |
| `009_users_telegram_dice_daily` | `users.telegram_dice_rolls_on`, `telegram_dice_roll_count` — shared daily cap for 🎰🎲🎯🎳🏀⚽ (see `Bot:TelegramDiceDailyLimit`) |
| `010_runtime_tuning` | `runtime_tuning` — JSON patch merged over file/env for whitelisted `Bot` + `Games` keys; edited from `/admin/settings` |
| `011_delivery_and_coordination` | `processed_updates`, `game_command_idempotency`, `mini_game_sessions`, `mini_game_roll_gates` |
| `012_telegram_dice_daily_per_game` | `telegram_dice_daily_rolls` with `(telegram_user_id, balance_scope_id, game_id)` PK for per-game daily caps |

New feature module migrations:

| Module | ID | Contents |
|---|---|---|
| `challenges` | `001_initial` | `challenge_duels` table plus indexes by `(chat_id, status, created_at)` and `(target_id, status, expires_at)`. Stores challenge participants, chat, stake, game, status, creation/expiry, response time, and completion time. |
| `pixelbattle` | `001_initial` | `pixelbattle_version_seq` and `pixelbattle_tiles` table. Each tile row stores `index`, `color`, monotonically increasing `version`, `updated_by`, and `updated_at`. |

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

### Horse GIF place labels

The shared `HorseRaceRenderer` now renders final placement information directly in the GIF for both normal horse races and 1v1 challenge horse races:

- Each horse gets a place when its progress first reaches 100%.
- The renderer draws a high-contrast badge near the horse (`1st`, `2nd`, `3rd`, etc.) and also repeats the place label in the right-side status column.
- `FinishHoldFrames` keeps the final state visible for a longer tail after the race is over, so Telegram replay/loop behavior clearly shows the placings.
- Ordinal suffix calculation is explicit: `(place % 100) is 11/12/13` uses `th`; otherwise `place % 10` picks `st`, `nd`, `rd`, or `th`.

Challenge horse races use the same renderer but with only two horses. The challenge handler delays the winner announcement by `frameCount × 60 ms + 1 s`, matching the GIF frame delay plus a small buffer. This prevents the text result from spoiling the race before the animation finishes.

## PixelBattle WebApp

Module **`Games.PixelBattle`** plus static files under `host/CasinoShiz.Host/wwwroot/pixelbattle`. `/pixelbattle` and `/pixelbattle/` redirect to `/pixelbattle/index.html`, so the normal ASP.NET static-file middleware serves the HTML/CSS/JS assets.

**Telegram entry point**

`/pixelbattle` sends a Telegram WebApp button. `Games:pixelbattle:WebAppUrl` must be a public HTTPS URL, usually:

```text
https://your-public-host/pixelbattle/index.html
```

Telegram WebApps do not work from a private LAN URL for other users. For local development, expose the bot with a tunnel such as Cloudflare Tunnel or ngrok. Old message buttons may keep old tunnel URLs and stale Telegram init data; send a fresh `/pixelbattle` command when testing after URL changes or after auth expiry.

**Auth**

API writes require Telegram WebApp `initData` in the `X-Telegram-Init-Data` header. `TelegramWebAppInitDataValidator` verifies the hash using the bot token and enforces `Games:pixelbattle:MaxInitDataAgeSeconds` (default 24 h in current config). Invalid or expired init data returns `401`. Valid init data still must belong to a known user in the current bot database; unknown users get `403`.

**Grid model**

- The grid is `200 × 160` pixels, so 32,000 logical tiles.
- Colors are restricted to the configured palette in `PixelBattleConstants`.
- `pixelbattle_tiles.index` is the primary key.
- Every update stores a color, updater Telegram user id, updated timestamp, and a version from `pixelbattle_version_seq`.
- `PixelBattleGrid` returns parallel `Tiles` and `Versionstamps` arrays.
- `PixelBattleUpdate` is `{ index, color, versionstamp }`.

**Frontend rendering**

The frontend originally used one DOM element per tile; it now uses a single `<canvas>`. The JS keeps `tiles` and `versionstamps` arrays in memory, draws each tile with `drawPixel`, and maps click coordinates back to a tile index. Canvas is much cheaper than 32,000 DOM nodes and keeps panning/painting responsive in the Telegram WebView.

**Live updates**

The `/pixelbattle/api/listen` endpoint is Server-Sent Events:

1. It writes `retry: 1000`.
2. It immediately streams a full-grid update.
3. It subscribes to the in-process `PixelBattleBroadcaster` and writes small update arrays as users paint.
4. It periodically sends a full grid again based on `Games:pixelbattle:FullUpdateIntervalMs`, so clients self-heal if a small update is missed.

The update endpoint (`POST /pixelbattle/api/update`) validates init data, body shape, tile index, color, and known user status, persists the tile, broadcasts the update, and returns the new versionstamp.

## Admin web UI

Razor pages under `/admin/*` served on the same port as the webhook (3000).

### Runtime settings (`/admin/settings`)

SuperAdmin can POST a JSON **patch** (whitelist: `Bot.DailyBonus`, `Bot.TelegramDiceDailyLimit`, and `Games` keys `dice`, `dicecube`, `darts`, `football`, `basketball`, `bowling`, `horse`, `transfer`). It is stored in `runtime_tuning.payload` and merged on top of `appsettings` / environment on every read via `IRuntimeTuningAccessor`. After save, the host reloads the overlay immediately.

Pending **🎲 cube** bets store `mult4`/`mult5`/`mult6` on the row at bet time so a rule change does not alter payout for throws already in flight.

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
- `/admin/people` — merged person view across known chats/scopes
- `/admin/groups` — known chat list
- `/admin/ledger` — economics ledger search/recovery
- `/admin/horse` — race control panel: today's bets, koefs, "Run race" button (SuperAdmin only)
- `/admin/horse/image` — horse race image/GIF preview endpoint
- `/admin/bets` — pending bets
- `/admin/challenges` — 1v1 PvP challenge tracking
- `/admin/history` — race history
- `/admin/events` — event log
- `/admin/settings` — live runtime JSON patch editor (SuperAdmin only)

### 1v1 challenge tracking (`/admin/challenges`)

The `1v1` admin page is host-owned Razor UI, backed by direct Dapper queries over `challenge_duels` and `known_chats`. It is read-only and available to both SuperAdmin and ReadOnly users through the normal admin gate.

Filters:

- **Game**: `Dice`, `DiceCube`, `Darts`, `Bowling`, `Basketball`, `Football`, `Slots`, `Horse`, `Blackjack`.
- **Status**: `Pending`, `Accepted`, `Completed`, `Declined`, `Failed`.
- **Chat**: distinct chats present in `challenge_duels`, labeled from `known_chats` when available.

Summary cards:

- Total duels for the current filter.
- Pending, accepted, completed, and cancelled counts.
- Total pot tracked, computed as `sum(amount * 2)`.

Tables:

- **By game type**: count, pending count, completed count, total pot, last created timestamp.
- **By chat**: chat label/id, count, pending/completed counts, total pot, last created timestamp.
- **Recent duels**: newest 500 rows with creation time, game, status, chat, both players, per-player stake, pot, completion time, and short challenge id.

This page is intended for operational questions such as "which chat uses PvP most?", "which game type is popular?", "are there stuck accepted challenges?", and "how much pot volume is flowing through 1v1".

## Configuration

`appsettings.json` is the source of truth; env vars override (Docker: `environment` block + `.env` file; K8s: Secret-backed env).

### `Bot` section

| Key | Required | Description |
|---|---|---|
| `Token` | yes | Telegram bot API token |
| `Username` / `BotUsername` | yes | Bot @username (with or without @); `BotUsername` is accepted as a config alias and used in `appsettings.json` |
| `Admins` | yes | List of Telegram user IDs with SuperAdmin access |
| `ReadOnlyAdmins` | no | List of Telegram user IDs with ReadOnly access |
| `AdminWebToken` | no | Token for password-based admin login |
| `IsProduction` | no | `true` → webhook; `false` → polling (default) |
| `WebhookPort` | no | Kestrel port in webhook mode (default 3000) |
| `TrustedChannel` | no | @username for race GIF broadcast |
| `StartingCoins` | no | Coins for new users (default 100) |
| `DailyBonus` (nested) | no | `Enabled`, `PercentOfBalance` (% of balance, e.g. `0.35` = 0.35 %), `MaxBonus`, `TimezoneOffsetHours` |
| `TelegramDiceDailyLimit` (nested) | no | `MaxRollsPerUserPerDayByGame` maps `dice`, `dicecube`, `darts`, `football`, `basketball`, `bowling` to per-day caps (**0** = unlimited for that game); `MaxRollsPerUserPerDay` is only a fallback for missing game ids; `TimezoneOffsetHours` follows `DailyBonus` — per wallet (chat scope) |

### `Games` section (excerpt)

| Key | Description |
|---|---|
| `dice:Cost` | 🎰 slot spin stake (before gas) |
| `*:RedeemDropChance` | Chance per resolved sticker game roll/throw to drop a copy-paste `/redeem <uuid>` code for that same game (`0.02` = 2%) |
| `dicecube:Mult4` / `Mult5` / `Mult6` | Pay multipliers for faces 4–6 on `/dice` + 🎲 |
| `dicecube:MaxBet`, `MinSecondsBetweenBets` | Stake cap and per-chat cooldown |
| `horse:*` | See below |
| `challenges:*` | See below |
| `pixelbattle:*` | See below |
| `transfer:*` | `FeePercent`, `MinFeeCoins`, `MinNetCoins`, `MaxNetCoins` for `/transfer` |

**`Games:horse`** — `HorseCount`, `MinBetsToRun`, `AnnounceDelayMs`, `TimezoneOffsetHours`, `Admins` (Telegram user IDs allowed to `/horserun`), `AutoRunEnabled`, `AutoRunLocalHour`, `AutoRunLocalMinute`. When `AutoRunEnabled` is true, `HorseScheduledRaceJob` runs **one global** race per calendar day after the configured local time, if there are enough bets (`MinBetsToRun`). It settles payouts like `/horserun global` and posts the result GIF only to chats that placed bets.

**`Games:challenges`**

| Key | Default | Notes |
|---|---:|---|
| `MinBet` | `1` | Rejects smaller challenge stakes. |
| `MaxBet` | `5000` | Rejects larger challenge stakes. |
| `HouseFeeBasisPoints` | `200` | Basis points on the combined pot; `200` = 2%. Integer division truncates fractional coins. |
| `PendingTtlMinutes` | `10` | Pending challenge expiry, clamped to 1–60 minutes by `ChallengeOptions.PendingTtl`. |

**`Games:pixelbattle`**

| Key | Default | Notes |
|---|---:|---|
| `WebAppUrl` | empty | Public HTTPS Telegram WebApp URL. Must point to `/pixelbattle/index.html` in practice. |
| `FullUpdateIntervalMs` | implementation default | Interval for periodic full-grid SSE snapshots. |
| `MaxInitDataAgeSeconds` | implementation default | Maximum age of Telegram WebApp init data. Expired init data returns `401`. |

Other games (darts, bowling, football, …) use multipliers in their **service** classes unless bound to options.

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

# Full stack (Postgres + Redis + ClickHouse + Prometheus + Grafana)
# Create/fill .env with Bot__Token, Bot__BotUsername (or Bot__Username), Bot__Admins__0
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
| Prometheus | 9090 | `http://localhost:9090` | Exporter, cAdvisor, and .NET runtime metrics |
| Grafana | 3001 | `http://localhost:3001` | Dashboards (admin/admin) |

`dotnet-monitor` runs as an internal compose service and exposes only its Prometheus metrics endpoint to the Docker network. The bot uses `DOTNET_DiagnosticPorts=/diag/dotnet-monitor.sock,nosuspend`, so metrics are collected when the monitor is available without blocking bot startup if the monitor is absent.

Compose also runs `postgres-exporter`, `redis-exporter`, `cadvisor`, and `dotnet-monitor` for Prometheus-backed Grafana dashboards. `db-backup` is a manual/one-shot helper that writes a `pg_dump` to the repository root when explicitly run.

Provisioned Grafana dashboards:

- `overview.json` — ClickHouse event overview
- `infra-pg-redis.json` — PostgreSQL and Redis exporter metrics
- `infra-services.json` — cAdvisor container CPU/memory plus connection panels
- `dotnet-runtime.json` — `dotnet-monitor` runtime metrics for the bot

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

680+ xUnit tests under `tests/CasinoShiz.Tests/`. No external database in CI — games use in-memory fakes (`FakeEconomicsService`, `InMemoryBlackjackHandStore`, etc.). `DailyBonusMath` unit-tests the bonus coin formula.

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~HandEvaluatorTests"
dotnet test --filter "DisplayName~majorityJa"
```

Coverage: domain logic (poker, secret hitler, blackjack, dice), services, taxes, PRNG, Russian plurals, router attribute scanning, InProcessEventBus, all framework contracts.

## Database schema

All game/balance persistence is PostgreSQL + Dapper. EF Core packages and a generic `EfRepository<T>` are present for optional module-owned repositories, but the shipped game hot paths and framework tables documented below are Dapper-backed.

### Framework tables

#### `users`

| Column | Type | Notes |
|---|---|---|
| `telegram_user_id` | BIGINT | part of **composite PK** with `balance_scope_id` |
| `balance_scope_id` | BIGINT | usually `Chat.Id` — separate wallet per chat |
| `display_name` | TEXT NOT NULL | |
| `coins` | INTEGER NOT NULL DEFAULT 0 | written only through `EconomicsService` (or `IDailyBonusService` in the same pattern) |
| `version` | BIGINT NOT NULL DEFAULT 0 | bumped on every balance mutation |
| `last_daily_bonus_on` | DATE NULL | last `/daily` claim in configured offset “day” |
| `created_at` | TIMESTAMPTZ | |
| `updated_at` | TIMESTAMPTZ | |

#### `economics_ledger`

Append-only audit of balance changes: `delta`, `balance_after`, `reason` (e.g. `dice.stake`, `daily.bonus`, `admin.adjust`). See migration `006`.

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

#### `known_chats`

Stores chats seen by the update pipeline. Admin pages use it to label chat ids with chat type, title, or username. The 1v1 admin page joins `challenge_duels.chat_id` to this table for readable chat labels.

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

Day-scoped on `race_date` (`MM-dd-yyyy` in `Games:horse:TimezoneOffsetHours`). Bets include `balance_scope_id` (Telegram chat). Results are keyed on `(race_date, balance_scope_id)`; scope `0` is the **global** merged race (all chats). Other scopes are per-group races. `file_id` stores the Telegram animation reference.

#### `challenge_duels`

Owned by `Games.Challenges`. One row per 1v1 PvP challenge.

| Column | Type | Notes |
|---|---|---|
| `id` | UUID | Primary key, embedded in callback data as compact `N` format. |
| `chat_id` | BIGINT | Chat/wallet scope where the challenge was created and settled. |
| `challenger_id` / `target_id` | BIGINT | Telegram user ids. |
| `challenger_name` / `target_name` | TEXT | Display names captured at challenge creation. |
| `amount` | INTEGER | Per-player stake; pot is `amount * 2`. |
| `game` | TEXT | Serialized `ChallengeGame` enum name. |
| `status` | TEXT | `Pending`, `Accepted`, `Declined`, `Completed`, or `Failed`. |
| `created_at` | TIMESTAMPTZ | Creation time. |
| `expires_at` | TIMESTAMPTZ | Pending challenges cannot be accepted after this time. |
| `responded_at` | TIMESTAMPTZ NULL | First accept/decline/failure transition time. |
| `completed_at` | TIMESTAMPTZ NULL | Set when the row reaches `Completed`, `Failed`, or `Declined`. |

Indexes:

- `(chat_id, status, created_at DESC)` for admin/chat tracking.
- `(target_id, status, expires_at)` for target-side pending lookup.

#### `pixelbattle_tiles`

Owned by `Games.PixelBattle`. Sparse table for painted tiles; missing rows render as the default color.

| Column | Type | Notes |
|---|---|---|
| `index` | INTEGER | Primary key; flattened index in a 200 × 160 grid. |
| `color` | TEXT | Hex color from the allowed palette. |
| `version` | BIGINT | Monotonic version from `pixelbattle_version_seq`. |
| `updated_by` | BIGINT | Telegram user id from validated WebApp init data. |
| `updated_at` | TIMESTAMPTZ | Last paint timestamp. |

#### `redeem_codes`

| Column | Type | Notes |
|---|---|---|
| `code` | UUID | PRIMARY KEY |
| `active` | BOOLEAN | false once redeemed |
| `issued_by` | BIGINT | |
| `free_spin_game_id` | TEXT | same-game extra roll target (`dice`, `darts`, `bowling`, …) |
| `redeemed_by` | BIGINT NULL | |

## Bot commands

All UI in Russian. Command names are ASCII.

### Everyone

| Command | Effect |
|---|---|
| `🎰` | Spin the slot machine — `Games:dice:Cost` + gas, fixed prize table, optional same-game redeem-code drop |
| `/dice bet <amount>` | Bot sends `🎲` after bet (reply to you); you can still send your own `🎲`. 4→×1, 5→×2, 6→×2 (`Games:dicecube:Mult*`) |
| `/darts bet <amount>` | Bot sends `🎯` or you throw. 4→×1, 5→×2, 6→×2 |
| `/football bet` / `/basket bet` | Bot sends ⚽/🏀 or you throw. 4→×2, 5→×2 (uniform 1…5) |
| `/bowling bet` | Bot sends `🎳` or you throw. 4→×1, 5→×2, 6 (strike)→×2 |
| `/horse bet <1-N> <amount>` | Bet on today's race |
| `/horse info` | This chat's pool: stakes + koefs |
| `/horse result` | This chat's last race GIF, else today's global race |
| `/poker …` | Texas Hold'em — create / join / start / fold / call / raise / check / leave |
| `/blackjack <bet>` | Start a hand; inline keyboard drives hit / stand / double |
| `/sh …` | Secret Hitler (5–10 players) — create / join / start / nominate / vote / leave |
| `/challenge @user <amount> <game>` | Create a 1v1 PvP stake challenge; supported games: dice/dicecube, darts, bowling, basketball, football, slots, horse, blackjack |
| `/challenge <amount> <game>` | Same as above, when replying to the target user's message |
| `/pixelbattle` | Open the PixelBattle Telegram WebApp button |
| `/transfer <target> <amount>` | Send coins to another user in the same group wallet; amount is recipient net amount |
| `/redeem <uuid>` | Redeem one extra roll for the code's game (private chat only, emoji captcha) |
| `/balance` | Current coin balance (this chat’s wallet) |
| `/daily` | Once per day (after offset): small % of balance, capped — see `Bot:DailyBonus` |
| `/top` | Per-chat leaderboard |
| `/help` | Command reference |

### Bot-admin-only (`Bot:Admins`)

| Command | Effect |
|---|---|
| `/horserun` | In a **group/supergroup**: run race for **this chat's** pool only. In **private**: global (all chats with bets). |
| `/horserun global` (or `all`) | Global merged race; result GIF is posted only to chats that placed bets |
| `/codegen [count]` | Generate copy-paste-ready `/redeem <uuid>` freespin codes |
| `/run pay <id> <amount>` | Manual coin adjustment |
| `/run userinfo` | Reply to message → Telegram user ID |
| `/run cancel_blackjack <id>` | Refund and remove stuck hand |
| `/run kick_poker <id>` | Remove from table and refund stack |
| `/rename <old> <new\|*>` | Display-name override; `*` clears |

## Recent Feature Notes

These notes summarize the latest large feature additions and the places they touch.

### PixelBattle changes

- New module: `games/Games.PixelBattle`.
- Host wiring: `Program.cs` registers `PixelBattleModule` and calls `app.MapPixelBattle()`.
- Static assets: `host/CasinoShiz.Host/wwwroot/pixelbattle/index.html`, `app.js`, `styles.css`.
- Rendering: the grid is now canvas-based instead of 32,000 DOM nodes.
- Backend: `/pixelbattle/api/grid`, `/pixelbattle/api/update`, `/pixelbattle/api/listen`.
- Live updates: in-process broadcaster plus SSE, with periodic full snapshots.
- Config required for Telegram users: `Games:pixelbattle:WebAppUrl`.

### Challenge system changes

- New module: `games/Games.Challenges`.
- Host wiring: `ChallengeModule` is registered in `Program.cs`, referenced by the host project and solution, and copied by Docker build metadata.
- Database: `challenge_duels`.
- User entry: `/challenge`.
- Callback data shape: `ch:a:<challengeIdN>` for accept, `ch:d:<challengeIdN>` for decline.
- Settlement reasons in the economics ledger: `challenge.stake`, `challenge.payout`, `challenge.tie_refund`, `challenge.refund`.
- Admin tracking: `/admin/challenges`, nav label `1v1`.

### Horse renderer changes

- Shared renderer now shows final placements directly in the GIF.
- Final frames are held longer so Telegram animation replay clearly shows the places.
- Challenge horse races use the same GIF path and delay winner announcement until the animation should have finished.

### Blackjack challenge changes

- Normal `/blackjack` remains the existing single-player turn-based game.
- Challenge blackjack is a separate instant duel inside `Games.Challenges`.
- It reuses `Games.Blackjack.Domain.Deck` and `BlackjackHandValue`, but does not create `blackjack_hands` rows.
- The output uses friendly suit symbols instead of raw card codes.

## Conventions

- User strings: Russian, live in each module's `Locales.cs` — never inline
- Plural forms: `RussianPlural.Plural(n, ["монета","монеты","монет"])`
- Randomness: Telegram dice outcomes come from Telegram; blackjack/poker decks use `RandomNumberGenerator`; horse winner/place generation uses the horse generator path and renders deterministic speed series for the chosen places
- No `Task.Delay` for scheduling — use `IBackgroundJob` / `IHostedService` sweepers
- Services return result records; handlers map to messages. Only throw for programmer errors
- **Balance changes go through `IEconomicsService` — always.** Never raw SQL on `users.coins`
- Logging: source-generated `[LoggerMessage]` only. No string interpolation in log calls
- Primary constructors are the default style: `public sealed class Foo(Dep dep) : IBar`
- Services: `Scoped`; hosted services / analytics: `Singleton`; `ITelegramBotClient`: `Singleton`
