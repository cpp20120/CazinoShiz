# CasinoShiz

A Telegram casino / mini-game bot. Russian-language UI, ASP.NET Core 10. Implements:

| Family | Commands | Chips |
|---|---|---|
| Telegram-dice family | 🎰 (slots) · `/dice` 🎲 · `/darts` 🎯 · `/football` ⚽ · `/basket` 🏀 · `/bowling` 🎳 | Native Telegram dice rolls, optional drop of `/redeem` codes |
| Multi-player table games | `/poker` · `/blackjack` · `/sh` (Secret Hitler) | Full DDD split (Domain / Application / Presentation) |
| Race | `/horse` (`bet` / `info` / `result`) · admin `/horserun` | SkiaSharp GIF renderer + scheduled daily auto-run |
| Casino-style pick games | `/pick` · `/picklottery` + `/pickjoin` · `/dailylottery` | Random outcome with configurable house edge, double-or-nothing chain, streak bonus, multi-user pools |
| PvP | `/challenge` | 1v1 stake duel layered on top of every other game |
| Webapp | `/pixelbattle` | Telegram WebApp shared pixel canvas |
| Economy | `/balance` · `/daily` · `/transfer` · `/redeem` · `/top` | Per-chat wallets, daily bonus drip, peer transfer with fee |
| Admin Telegram commands | `/topall` · `/analytics` · `/chats` · `/codegen` · `/run …` · `/rename …` · `/horserun` | Restricted to `Bot:Admins` |

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | **PostgreSQL 16** via Dapper on the live game/balance paths (`SELECT ... FOR UPDATE` on the wallet hot path). EF Core packages and `EfRepository<T>` exist for optional module-owned repositories. |
| Migrations | Dapper-based, tracked in `__module_migrations`, applied at startup by `ModuleMigrationRunner` |
| Event bus | DotNetCore.CAP 10.x — PostgreSQL outbox + Redis transport when `Redis:Enabled=true`; `InProcessEventBus` fallback for single-instance / dev |
| Update fan-out | Redis Streams (opt-in via `Redis:Enabled`) — partitioned by `chatId % N`, consumer groups |
| Analytics | ClickHouse 24.x via `ClickHouse.Client` 7.x (buffered, degrades gracefully) |
| Dashboards | Grafana 11 with auto-provisioned ClickHouse + Prometheus datasources |
| Graphics | SkiaSharp 3.x (horse race GIF renderer, offloaded to thread pool) |
| Tests | xUnit, 680+ tests covering domain + services + router + framework |
| Deploy | Docker Compose (bot + postgres + redis + clickhouse + prometheus + grafana) / Helm chart |

## Layout

```
CasinoShiz/
├── docker-compose.yml                — bot + db/cache/analytics + monitoring stack
├── Dockerfile                        — dotnet/sdk:10.0 multi-stage
├── CasinoShiz.slnx                   — solution manifest
├── .env / .env.example               — local env file consumed by compose (.env is git-ignored)
├── prometheus/                       — scrape config for exporters, cAdvisor, dotnet-monitor
├── grafana/                          — datasource + dashboards provisioning
├── helm/                             — Kubernetes Helm chart
│   ├── values.yaml
│   ├── values.secret.yaml            — secrets (git-ignored, never commit)
│   └── templates/
├── docs/docs.md                      — this document
├── db/clickhouse/                    — manual ClickHouse migration scripts
├── framework/
│   ├── BotFramework.Sdk/             — module contracts: IModule, IUpdateHandler, route attrs,
│   │                                   IEconomicsService, IAnalyticsService, IDomainEventBus, …
│   ├── BotFramework.Sdk.Testing/     — xUnit helpers for pure-domain tests
│   └── BotFramework.Host/            — ASP.NET host, pipeline/router, economics, economics_ledger,
│                                       IDailyBonusService, IRuntimeTuningAccessor, analytics, CAP,
│                                       Redis Streams, admin pages, migrations runner
├── games/
│   ├── Games.Dice/                   — slots 🎰
│   ├── Games.DiceCube/               — dice cube 🎲
│   ├── Games.Darts/                  — darts 🎯
│   ├── Games.Football/               — football ⚽
│   ├── Games.Bowling/                — bowling 🎳
│   ├── Games.Basketball/             — basketball 🏀
│   ├── Games.Blackjack/              — blackjack
│   ├── Games.Horse/                  — horse racing + GIF renderer
│   ├── Games.Poker/                  — Texas Hold'em (DDD split)
│   ├── Games.SecretHitler/           — hidden-role social deduction (DDD split)
│   ├── Games.Pick/                   — /pick, /picklottery + /pickjoin, /dailylottery
│   ├── Games.Challenges/             — 1v1 PvP challenge layer over the other games
│   ├── Games.PixelBattle/            — Telegram WebApp pixel canvas + SSE updates
│   ├── Games.Redeem/                 — freespin codes + emoji captcha
│   ├── Games.Leaderboard/            — /top, /topall, /balance, /daily, /help
│   ├── Games.Transfer/               — /transfer (peer coins in groups)
│   └── Games.Admin/                  — admin Telegram commands (/run, /rename, /chats,
│                                       /analytics)
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

`BotFrameworkBuilder.AddBotFramework()` registers framework singletons (Telegram client, router, pipeline, middlewares, `IEconomicsService`, `IDailyBonusService`, `IRuntimeTuningAccessor`, event bus, Redis Streams, admin auth, migrations runner). Each `.AddModule<T>()` instantiates the module, runs `ConfigureServices(IModuleServiceCollection)`, and folds its handlers/locales/migrations/commands/admin pages into the shared aggregate. The host also registers `DebugModule` before the game modules. `UseBotFramework()` wires the webhook endpoint, health endpoints, session middleware, and admin gate.

### Request flow

```mermaid
flowchart TD
    A[Telegram] -->|Polling / Webhook| B(BotHostedService)
    B --> C{Redis:Enabled?}
    C -- Yes --> D[UpdateStreamPublisher]
    D -->|Redis Stream| E[UpdateStreamWorkerService]
    E --> F[UpdatePipeline]
    C -- No --> F
    
    F --> G[ExceptionMiddleware]
    G --> H[LoggingMiddleware]
    H --> I[RateLimitMiddleware]
    I --> J[UpdateRouter]
    
    J -->|Attribute Match| K[IUpdateHandler.HandleAsync]
    K --> L[Feature Service e.g. DiceService]
    L --> M[(PostgreSQL / Dapper)]
    L --> N[IEconomicsService]
    L --> O[IAnalyticsService]
    O --> P[(ClickHouse)]
```

When `Redis:Enabled=true`, the polling loop and webhook both publish updates to Redis Streams instead of dispatching inline. `UpdateStreamWorkerService` reads from N partitioned streams (keyed by `chatId % N`) in consumer groups and invokes the same `UpdatePipeline`.

### Event bus

`IDomainEventBus.PublishAsync(IDomainEvent)` — cross-module domain events.

```mermaid
flowchart LR
    A[Publisher Service] -->|PublishAsync| B(IDomainEventBus)
    B --> C{Redis:Enabled?}
    
    C -- No --> D[InProcessEventBus]
    D --> E[Subscribers]
    
    C -- Yes --> F[CapEventBus]
    F --> G[(PostgreSQL Outbox)]
    G -->|CAP Relay| H((Redis Streams Topic))
    H -->|CAP Consumer| I[CapEventConsumer]
    I --> E
```

- **Redis enabled**: DotNetCore.CAP with PostgreSQL outbox + Redis Streams transport. At-least-once delivery across pods. `CapEventBus` resolves a scoped `ICapPublisher` per publish call via `IServiceScopeFactory`. `CapEventConsumer` receives all events on the `"domain.event"` topic and dispatches to pattern-matched subscribers.
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

Longer command prefix wins (`/horserun` > `/horse`, `/picklottery` > `/pick`). Adding a handler: implement `IUpdateHandler`, decorate with route attribute, register via `services.AddHandler<T>()` in the owning module's `ConfigureServices`. No host edit required.

### Concurrency

Stateful game writes are serialized with `SemaphoreSlim(1,1)` per game instance stored in a `ConcurrentDictionary<TKey, Gate>`. The `Gate` wrapper tracks `LastUsedTick` for cleanup. Stale gates are pruned by sweeper jobs:

- Poker: `PokerTurnTimeoutJob.SweepAsync` calls `PokerService.PruneGates(3 × turnTimeoutMs)`
- Blackjack: `BlackjackHandTimeoutJob.SweepAsync` calls `BlackjackService.PruneGates(handTimeoutMs)`
- Secret Hitler: `SecretHitlerGateCleanupJob` runs every 10 min, prunes gates idle > 1 hour
- Pick lottery: `PickLotterySweeperJob` polls `pick_lottery` for expired open pools
- Daily lottery: `PickDailyLotterySweeperJob` polls `pick_daily_lottery` for pools whose draw deadline has passed

Balance mutations acquire a Postgres row lock via `SELECT ... FOR UPDATE` inside `EconomicsService`.

## Economics (balance bounded context)

Wallets are scoped per **Telegram chat** (`balance_scope_id` = `Message.Chat.Id` in public groups/supergroups; in private DMs it equals the user id). The same person has separate coin balances in different chats.

`IEconomicsService` is the **only** place routine balance mutations go. All game and admin pay flows use it; every successful change appends a row to `economics_ledger` in the same transaction as `users.coins` / `version`.

```csharp
await economics.EnsureUserAsync(userId, balanceScopeId, displayName, ct);
await economics.TryDebitAsync(userId, balanceScopeId, amount, "horse.bet", ct);
await economics.CreditAsync(userId, balanceScopeId, payout, "dice.prize", ct);
await economics.AdjustUncheckedAsync(userId, balanceScopeId, delta, ct); // admin / recovery
// Atomic two-party move (used by /transfer): debit sender and credit recipient in one transaction.
await economics.TryPeerTransferAsync(fromId, toId, balanceScopeId, debitTotal, creditNet, "transfer.send", "transfer.receive", ct);
```

`EnsureUserAsync` seeds new `(user, scope)` rows with `Bot:StartingCoins` (default **100**) and caches existence for 24 h in `IMemoryCache`.

Direct writes to `users.coins` via raw SQL are **forbidden**; `EconomicsService` uses `SELECT … FOR UPDATE` and bumps `version` on every mutation. Admin UI writes still go through `IEconomicsService`; `admin_audit` records admin web actions.

Economics ledger reasons (used as a low-cardinality string tag — also useful for analytics):

| Reason | Source |
|---|---|
| `dice.stake` / `dice.prize` | Slots (🎰) |
| `dicecube.stake` / `dicecube.payout` | `/dice` 🎲 |
| `darts.stake` / `darts.payout` | `/darts` |
| `football.stake` / `football.payout` | `/football` |
| `basketball.stake` / `basketball.payout` | `/basket` |
| `bowling.stake` / `bowling.payout` | `/bowling` |
| `horse.bet` / `horse.payout` | `/horse` + `/horserun` |
| `poker.buyin` / `poker.payout` / `poker.refund` | `/poker` |
| `blackjack.stake` / `blackjack.payout` / `blackjack.refund` | `/blackjack` |
| `sh.buyin` / `sh.winnings` / `sh.refund` | `/sh` |
| `pick.stake` / `pick.payout` | `/pick` (single) |
| `pick.lottery.stake` / `pick.lottery.payout` / `pick.lottery.refund` | `/picklottery`, `/pickjoin` |
| `pick.daily.stake` / `pick.daily.payout` / `pick.daily.refund` | `/dailylottery` |
| `challenge.stake` / `challenge.payout` / `challenge.tie_refund` / `challenge.refund` | `/challenge` |
| `transfer.send` / `transfer.receive` | `/transfer` |
| `daily.bonus` | `/daily` |
| `admin.adjust` | Admin UI / `/run pay` |

### Free-coin sources (faucets)

The bot has only three coin sources outside game payouts:

1. **`Bot:StartingCoins`** (default `100`) — once per `(user, balance_scope)` row on first observation. Capped naturally by user count.
2. **`/daily`** — `floor(balance × PercentOfBalance / 100)` capped by `MaxBonus`. With the default `PercentOfBalance = 0.35` this is **0.35% of balance per day** (the formula divides by 100 internally — see "Daily bonus" below). Once per local day per `(user, balance_scope)`.
3. **`/redeem`** — does **not** mint coins. It only grants one extra Telegram-dice roll for the same game id by decrementing `telegram_dice_daily_rolls.roll_count` by `1`. Net effect: `+1` to the daily roll cap.

There is no other inflow. Game payouts move existing coins between players or out to the house (loss → coins leave circulation).

### House edge (Telegram mini-games)

Mini-games using Telegram's random dice (🎲 🎯 🎳 🏀 ⚽) are tuned so that, if each face is roughly uniform, expected return per unit bet is **below 100%** in the long run. Concrete multipliers and slot stake live in the per-game service code or `appsettings.json` under `Games:<game>`. Default RTP for the shipped configuration:

| Game | Payout map | Distribution | RTP |
|---|---|---|---|
| 🎰 slots (`/dice`) | 64-outcome prize table at `Cost = 10` (gas tax adds 0 at this stake) | uniform 1..64 | ~95.3% |
| `/dice` 🎲 (DiceCube) | `Mult4 = 1`, `Mult5 = 2`, `Mult6 = 2`, others `0` | uniform d6 | ~83.3% |
| `/darts` | `4 → ×1`, `5,6 → ×2`, others `0` | uniform d6 | ~83.3% |
| `/bowling` | same as darts | uniform d6 | ~83.3% |
| `/football` | `4,5 → ×2`, others `0` | uniform d5 | 80% |
| `/basket` | same as football | uniform d5 | 80% |
| `/pick` | `bet × N/k × (1 − HouseEdge)` floored | uniform among variants | ≤ 95% (default `HouseEdge = 0.05`) |
| `/picklottery`, `/dailylottery` | winner takes `pot − floor(pot × HouseFeePercent)` | uniform among entries | 95% (default fee = 5%) |
| `/horse` | parimutuel with `1.1` divisor + integer floor | RNG winner | ~9% structural skim |
| `/challenge` | winner takes `pot − HouseFeeBasisPoints / 10000 × pot` | game-dependent | ~98% (default fee = 200 bps = 2%) |
| `/poker` | zero-sum among seated players | shuffled deck | 100% (no rake) |
| `/sh` | zero-sum among players (winners split pot) | shuffled deck | 100% (no rake) |
| `/blackjack` | rules-driven (3:2 BJ, push, double, dealer hits to 17) | shuffled deck | strategy-dependent |

`DailyBonus` is a controlled drip of coins (small % with cap), so it does not erase the house edge in aggregate.

### Daily bonus (`/daily`)

`IDailyBonusService` (framework) credits a small once-per-local-day bonus.

```
floor(balance × PercentOfBalance / 100), capped by MaxBonus
```

Note the **`/100`** in the formula: `Bot:DailyBonus:PercentOfBalance = 0.35` means **0.35%**, not 35%. Ledger reason `daily.bonus`. The "day" boundary uses `Bot:DailyBonus:TimezoneOffsetHours` (default `7`, aligned with horse racing). Column `users.last_daily_bonus_on` (migration `008_users_last_daily_bonus`) stores the last claim.

- If the formula rounds to `0` (tiny balance) but the user is non-empty, **the day is not marked**, so they can run `/daily` again after their balance moves.
- **Balance 0** → no credit, day not marked.
- Already claimed for this day → friendly refusal.

Config: `Bot:DailyBonus` — `Enabled`, `PercentOfBalance`, `MaxBonus`, `TimezoneOffsetHours`. Handler: `LeaderboardHandler` on `/daily`.

### Telegram-dice daily caps

`Bot:TelegramDiceDailyLimit` — per-game daily roll caps (`MaxRollsPerUserPerDayByGame`, default `5` for each of the six dice games; `0` = unlimited). The fallback `MaxRollsPerUserPerDay` only applies if a game id has no per-game entry. The cap is **per wallet** (`balance_scope`), so a user has separate quotas in each chat.

Implementation: `TelegramDiceDailyRollLimiter` against the `telegram_dice_daily_rolls` table (composite PK `(telegram_user_id, balance_scope_id, game_id)`). The day boundary uses `TimezoneOffsetHours` (default `7`).

`TryConsumeRollAsync` is called before each paid roll; `LimitExceeded` blocks the throw. Admins (those with `userId == chatId` in private DM, i.e. talking to the bot in their own private chat) bypass the cap so they can test.

`GrantExtraRollAsync` is what `/redeem` calls on success — it decrements the counter so the next throw goes through, effectively granting one extra roll for the redeemed game.

## Modules in detail

### Slots (`Games.Dice`, 🎰)

User sends a Telegram 🎰 dice in any chat. `DiceHandler` rejects forwards (would otherwise let users replay frozen wins).

`DiceService.PlaceBetAndResolveAsync`:

1. Debits `Cost + GetGasTax(Cost)` with reason `dice.stake`. At `Cost = 10` the gas function rounds to `0`, so net loss per spin is `10`.
2. Decodes the 1..64 Telegram outcome into three reels of four symbols (bar / cherry / lemon / seven, each with its own stake price).
3. Looks up the prize from a fixed table: triple seven `→ 77`, triple lemon `→ 30`, triple cherry `→ 23`, triple bar `→ 21`; pair seven `→ 10 + sum`, pair lemon `→ 6 + sum`, other pair `→ 4 + sum`; otherwise `sum − 3`.
4. Credits the prize with reason `dice.prize` if `> 0`.
5. Optionally drops a `/redeem <uuid>` code via `TelegramMiniGameRedeemDrops` with probability `Games:dice:RedeemDropChance` (default `0.02`).

Config: `Games:dice:Cost`, `Games:dice:RedeemDropChance`. Telegram-dice cap: `dice` key.

### Dice cube (`Games.DiceCube`, `/dice` 🎲)

Two-step game: `/dice bet <amount>` → `dicecube_bets` row → user (or bot) sends `🎲` → resolution.

`DiceCubeService.PlaceBetAsync`:

1. Validates amount in `[1, MaxBet]`.
2. Enforces `MinSecondsBetweenBets` per chat through `IMemoryCache` to prevent griefing fast-spamming throws.
3. Debits stake, inserts a pending row.

`DiceCubeService.ResolveAsync` on the user's 🎲:

1. Reads pending bet.
2. `mults[face]` table from `Games:dicecube:Mult4..6`; faces 1–3 pay `0`.
3. Credits `bet × multiplier` with reason `dicecube.payout` (no stake return on win — net is just the payout).

Pending rows snapshot the multiplier in case the rule changes mid-flight (admin tuning).

Config: `Mult4`, `Mult5`, `Mult6`, `MaxBet`, `DefaultBet`, `MinSecondsBetweenBets`, `RedeemDropChance`.

### Darts / football / basketball / bowling

All four follow the same pattern as `/dice`: `[Command]` for the bet path (bot throws on the user's behalf) plus `[MessageDice("🎯/⚽/🏀/🎳")]` for the quick-play path (the user throws their own emoji and the bot resolves against the configured multipliers).

Multiplier maps:

- `/darts`, `/bowling`: `4 → ×1`, `5,6 → ×2`, `1..3 → 0` (uniform d6 → 5/6 RTP)
- `/football`, `/basket`: `4,5 → ×2`, `1..3 → 0` (Telegram returns 1..5 → 4/5 RTP)

Config per game: `MaxBet`, `DefaultBet`, `RedeemDropChance`. The Telegram-dice cap key matches the game id (`darts`, `football`, `basketball`, `bowling`).

### Horse racing (`Games.Horse`)

User commands:

| Command | Effect |
|---|---|
| `/horse bet <horse> <amount>` | Bet on horse 1..`HorseCount` for today's pool in this chat |
| `/horse info` | Show pool stakes and parimutuel-style koefs |
| `/horse result` | Show this chat's last race GIF (or today's global race) |
| `/horse help` | Rules text |

Race "day" uses `Games:horse:TimezoneOffsetHours` (default `7`). `HorseTimeHelper.GetRaceDate(offsetHours)` builds the `MM-dd-yyyy` pool key.

**Payout math** (`HorseService.GetKoefs`):

```
koef[h] = floor((sum − stake[h]) / (1.1 × stake[h]) × 1000) / 1000 + 1
```

This is parimutuel with a structural `1.1` divisor + integer floor — roughly a 9% rake plus truncation. The losing-stake mass redistributes to winners proportional to their stake.

**Race winner** uses `RandomNumberGenerator.Fill` for cryptographic-grade randomness; horses are drawn uniformly. The renderer (`HorseRaceRenderer`) draws SkiaSharp frames into an LZW GIF89a, offloaded with `Task.Run` so the async scheduler stays free. `HorseGifCache` keeps the latest race GIF in memory for `/horse result`.

**Place labels** are baked into the GIF: each horse gets a place when its progress first reaches 100%, and the renderer draws a high-contrast badge (`1st`, `2nd`, …) on the horse plus a status column on the right. `FinishHoldFrames` keeps the final state visible at the end of the loop. Ordinal suffix logic: `(place % 100) ∈ {11, 12, 13}` → `th`; otherwise `place % 10` picks `st`, `nd`, `rd`, `th`.

**Auto-run.** `HorseScheduledRaceJob` runs **one global** race per calendar day after `Games:horse:AutoRunLocalHour:Minute` (when `AutoRunEnabled = true` and there are at least `MinBetsToRun` bets across all chats). It settles like `/horserun global` and posts the result GIF only to chats that placed bets. Default schedule: 21:00 in UTC+7.

**Manual run.** `/horserun` (admin only):

- In a group/supergroup → run that chat's pool only.
- In a private chat → global merged race.
- `/horserun global` (or `all`) → explicitly global, even from a group.

### Poker (`Games.Poker`, DDD split)

Texas Hold'em over multiple chats. Full DDD split:

```
games/Games.Poker/
├── Domain/                  — pure hand-level logic, no DB / no I/O
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

Subcommands: `create`, `join`, `start`, `fold`, `call`, `raise <n>`, `check`, `leave`. Buy-in is debited on join with reason `poker.buyin`; `entry.Won` is credited at showdown with `poker.payout`. **No house rake** — the game is zero-sum among seated players.

`PokerTurnTimeoutJob` (`IBackgroundJob`) polls every 10 s for stuck hands and runs `DecideAutoAction` (check if possible, else fold). Same path handles restart recovery.

Config: `Games:poker:BuyIn`, blinds (1/2 default), `MaxPlayers` (default 8).

### Blackjack (`Games.Blackjack`)

Single-player turn-based hand against the dealer. `/blackjack <bet>` opens a hand, the bot DMs an inline keyboard for `Hit / Stand / Double`. The deck is `Deck.BuildShuffled()` per hand (52-card shoe re-shuffled each time).

Settlement (`BlackjackService.Resolve`):

| Outcome | Player credit |
|---|---|
| Player bust | 0 (loses stake) |
| Player BJ vs non-BJ dealer | `bet + bet × 3 / 2` (3:2 on natural) |
| Player BJ vs dealer BJ | `bet` (push) |
| Dealer bust | `bet × 2` |
| Player total > dealer total | `bet × 2` |
| Player total < dealer total | 0 |
| Tie (no BJ) | `bet` (push) |

Dealer hits while total `< 17`. **Splits are not implemented**; only `Double` on a two-card hand. There is no extra house rake — the edge comes from BJ rules + dealer-hits-to-17.

`BlackjackHandTimeoutJob` cleans up stale hands using `BlackjackService.PruneGates(handTimeoutMs)`.

Config: `Games:blackjack:MinBet`, `MaxBet`, `HandTimeoutMs`.

### Secret Hitler (`Games.SecretHitler`, DDD split)

5–10 player hidden-role social deduction game. Buy-in joins a pot; winners (liberals or fascists+Hitler depending on `game.Winner`) split the pot evenly with the remainder distributed `1` coin at a time to the first winners in enumeration order until exhausted.

```
games/Games.SecretHitler/
├── Domain/
│   ├── ShPolicyDeck.cs       (17 cards: 6L + 11F, serialized as "LFFL…" strings; auto-reshuffle)
│   ├── ShRoleDealer.cs       (player count → role distribution; uses RandomNumberGenerator)
│   └── ShTransitions.cs      (full state machine: StartGame, Nomination, Vote, PresidentDiscard,
│                                ChancellorEnact, FailElection, AdvancePresident)
├── Application/
│   ├── ShResults.cs          (ShError, ShGameSnapshot, typed result records)
│   └── SecretHitlerService.cs (Dapper + Gate + EconomicsService for buy-ins/refunds)
└── Presentation/
    ├── ShCommand.cs, ShCommandParser.cs, ShStateRenderer.cs
```

Subcommands: `create`, `join`, `start`, `nominate <user>`, `vote yes/no`, `leave`. Ledger reasons: `sh.buyin`, `sh.winnings`, `sh.refund` (cancelled / disbanded games).

**Zero-sum** — no rake; everything joining the pot eventually reaches winners.

`SecretHitlerGateCleanupJob` (`IBackgroundJob`) prunes idle gates every 10 min.

Config: `Games:sh:BuyIn`.

### Pick (`Games.Pick`, `/pick`)

User-supplied variant list. Bot picks one variant uniformly at random; if it matches the user's backed variant(s), pay out with house edge.

Command shapes (handled by `PickCommandParser`):

| Form | Effect |
|---|---|
| `/pick a, b, c` | Default bet; back the **first** variant |
| `/pick 50 a, b, c` | Bet `50`; back the first variant |
| `/pick bet 50 a, b, c` | Same, explicit form |
| `/pick a \| a, b, c` | Back by **name** (the part before `\|`) |
| `/pick 2 \| a, b, c` | Back by **position** 1..N |
| `/pick a, c \| a, b, c, d` | Multi-back parlay (two variants) |
| `/pick 50 a, c \| a, b, c, d` | Bet `50`, multi-back |
| `/pick help` | Usage |

**Payout** (`PickService.ComputePayout`):

```
gross = stake × N / k                   (fair odds: pay back the inverse of P(win) = k/N)
net   = gross × (1 − HouseEdge)
payout = floor(max(0, net))             (truncation favours house)
```

Where `N` = number of variants, `k` = number of variants the user backed.

**Streak bonus** (`PickStreakStore`, in-memory `ConcurrentDictionary` keyed on `(userId, chatId)`):

```
streak  = consecutive wins (capped at StreakCap)
bonus   = floor(stake × StreakBonusPerWin × min(streak − 1, StreakCap))
```

Defaults: `StreakBonusPerWin = 0.5`, `StreakCap = 4` → bonus tops out at the 5th consecutive win for `+2 × stake`. A loss resets the streak.

**Double-or-nothing chain** (`PickChainStore`, in-memory). After a win, the bot offers an inline `🔥 Удвоить ×2 ({stake_for_next})` button valid for `ChainTtlSeconds`. Clicking it re-stakes the entire payout against the same variant list. The chain depth is capped at `ChainMaxDepth` (default 5). Only the original user can click; the button is single-shot per offer. When the chain breaks (loss or expiry) the streak counter is also reset.

**Suspense reveal** (when `RevealAnimation = true`). The bot edits the message and eliminates losing variants one by one, with `RevealStepDelayMs` between steps and total cap `RevealMaxTotalMs`. For very many variants the cap shortens each step.

Config (section `Games:pick`):

| Key | Default | Notes |
|---|---:|---|
| `DefaultBet` | `10` | Used when `/pick a, b` has no number |
| `MaxBet` | `5000` | Hard cap; `0` would mean no cap |
| `MinVariants`, `MaxVariants` | `2`, `10` | Validation |
| `MaxVariantLength` | `64` | Trims excess characters per variant |
| `HouseEdge` | `0.05` | Fraction of gross win the house keeps |
| `StreakBonusPerWin`, `StreakCap` | `0.5`, `4` | Per-win bonus and cap |
| `ChainMaxDepth`, `ChainTtlSeconds` | `5`, `60` | Double-or-nothing limits |
| `RevealAnimation`, `RevealStepDelayMs`, `RevealMaxTotalMs` | `true`, `600`, `4500` | Eliminate-losers animation |

Ledger reasons: `pick.stake`, `pick.payout`. No rows in the DB; streak and chain state live in process memory and are safe to lose on restart (worst case: reset of a streak / chain that was in flight).

### Pick lottery (`Games.Pick`, `/picklottery` + `/pickjoin`)

Time-bounded multi-user lottery in one chat. The opener stakes a fixed amount; other players join with the same stake until the deadline; sweeper draws a random entrant.

| Subcommand | Effect |
|---|---|
| `/picklottery <stake>` | Open a pool with this stake; opener auto-enters |
| `/picklottery info` | Snapshot of the open pool (entrants, pot, time left) |
| `/picklottery cancel` | Cancel (only the opener); refunds everyone |
| `/picklottery help` | Usage |
| `/pickjoin` | Join the open pool |

**Settlement** (`PickLotteryService.SettlePoolAsync`, called by sweeper):

```mermaid
stateDiagram-v2
    [*] --> Open: /picklottery <stake>
    Open --> Open: /pickjoin (adds entrant)
    
    Open --> Settled: SweeperJob (deadline reached, ≥2 entrants)
    Open --> Cancelled: SweeperJob (deadline reached, <2 entrants)
    Open --> Cancelled: /picklottery cancel (opener only)
    
    Settled --> [*]: Winner gets pot minus fee
    Cancelled --> [*]: All stakes refunded
```

```
fee     = floor(pot × HouseFeePercent)
payout  = max(0, pot − fee)
winner  = entries[Random.Shared.Next(entries.Count)]
```

If at the deadline `entries.Count < MinEntrantsToSettle` (default 2), the pool is **cancelled** and all stakes are refunded. Same for explicit cancel by the opener.

**Storage**: `pick_lottery` (one open pool per chat, partial unique index `WHERE status = 'open'`) + `pick_lottery_entries` (PK `(lottery_id, user_id)`). The opener's stake is debited atomically with the pool insertion; subsequent joins debit + insert in their own transactions.

`PickLotterySweeperJob` polls every `Games:pick:Lottery:SweeperIntervalSeconds` (default 15s) and settles or cancels expired pools.

Config (section `Games:pick:Lottery`):

| Key | Default | Notes |
|---|---:|---|
| `DurationSeconds` | `300` | Pool lifetime (5 min) |
| `MinEntrantsToSettle` | `2` | Below this → cancel + refund |
| `HouseFeePercent` | `0.05` | Fraction of pot the house keeps |
| `MinStake`, `MaxStake` | `1`, `10000` | Stake bounds (`MaxStake = 0` → unlimited) |
| `SweeperIntervalSeconds` | `15` | How often the sweeper polls |

Ledger reasons: `pick.lottery.stake`, `pick.lottery.payout`, `pick.lottery.refund`.

### Daily lottery (`Games.Pick`, `/dailylottery`)

Per-chat suite of independent daily pools. Each chat has its own daily pool; tickets cost a fixed amount; the draw happens at a configurable local hour each day (default **18:00 in UTC+7**).

| Subcommand | Effect |
|---|---|
| `/dailylottery` | Status of today's pool in this chat |
| `/dailylottery buy [N]` | Buy N tickets (default 1) |
| `/dailylottery <N>` | Shortcut: same as `buy N` |
| `/dailylottery history` | Last `HistoryLimit` settled draws in this chat |
| `/dailylottery help` | Usage |

**"Today's pool"**. The cycle key is the **calendar date of the draw**. If the draw hour for the current local day has already passed, buys land in the **next day's** pool — there is no dead window. Concretely: with `DrawHourLocal = 18` and offset `+7`, anything bought 18:00..23:59 local goes into tomorrow's pool, which is settled at 18:00 local tomorrow.

**Settlement** (`PickDailyLotteryService.SettlePoolAsync`):

```
pot     = sum(price_paid)            from pick_daily_lottery_tickets
fee     = floor(pot × HouseFeePercent)
payout  = max(0, pot − fee)
winner  = ORDER BY random() LIMIT 1  weighted by ticket count (one row per ticket)
```

If a draw fires with zero tickets → cancelled, no draw. If a chat has tickets bought but the draw deadline arrives, settlement always picks one ticket-winner, weighted by ticket count.

**Storage**: `pick_daily_lottery` (one pool per `(chat_id, day_local)` via a unique index) + `pick_daily_lottery_tickets` (one row per ticket, simple `BIGSERIAL` PK). Random draw is `ORDER BY random() LIMIT 1` over the tickets table, which is naturally weight-by-ticket-count.

`PickDailyLotterySweeperJob` polls every `Games:pick:Daily:SweeperIntervalSeconds` (default 60s) for pools whose `deadline_at` has passed and settles or cancels them.

Config (section `Games:pick:Daily`):

| Key | Default | Notes |
|---|---:|---|
| `TicketPrice` | `50` | Coins per ticket |
| `MaxTicketsPerUserPerDay` | `0` (unlimited) | Hard cap on a user's tickets in one daily pool |
| `MaxTicketsPerBuyCommand` | `100` | Throttle one-shot purchases |
| `HouseFeePercent` | `0.05` | Fraction of pot the house keeps |
| `SweeperIntervalSeconds` | `60` | How often the sweeper polls |
| `HistoryLimit` | `7` | How many history entries `/dailylottery history` shows |
| `DrawHourLocal` | `18` | 0..23, local draw hour |
| `TimezoneOffsetHoursOverride` | `0` | Non-zero overrides the global `Bot:TelegramDiceDailyLimit:TimezoneOffsetHours` |

Ledger reasons: `pick.daily.stake`, `pick.daily.payout`, `pick.daily.refund`.

### Challenges (`Games.Challenges`, `/challenge`)

1v1 PvP layer over every other game. Owns its own `challenge_duels` table and settlement flow, but balance moves still go through `IEconomicsService`.

Command shapes:

```
/challenge @username 500 dicecube
/challenge 500 darts        # reply to the target user's message
/challenge help             # usage
```

Target resolution: explicit `@username` first, else reply-to-message. Username lookup uses the shared `users` table scoped to the current chat (`balance_scope_id = chat_id`), so the target generally needs prior bot interaction in that chat. Reply-based challenges always work because Telegram gives the target user id directly.

**Supported games and aliases**:

| Game enum | User aliases | Telegram / rendering path |
|---|---|---|
| `DiceCube` | `dice`, `die`, `dicecube`, `cube`, `кубик`, 🎲 | Telegram `SendDice` with 🎲 |
| `Darts` | `darts`, `dart`, `дартс`, 🎯 | Telegram `SendDice` with 🎯 |
| `Bowling` | `bowling`, `bowl`, `боулинг`, 🎳 | Telegram `SendDice` with 🎳 |
| `Basketball` | `basket`, `basketball`, `баскетбол`, 🏀 | Telegram `SendDice` with 🏀 |
| `Football` | `football`, `soccer`, `футбол`, ⚽ | Telegram `SendDice` with ⚽ |
| `Slots` | `slots`, `slot`, `casino`, `слоты`, 🎰 | Telegram `SendDice` with 🎰 |
| `Horse` | `horse`, `horses`, `race`, `лошади`, `скачки`, 🐎 | 2-horse SkiaSharp GIF |
| `Blackjack` | `blackjack`, `bj`, `21`, `блекджек`, 🃏 | Auto-resolved crypto-shuffled BJ duel |

**Lifecycle**:

1. `CreateAsync` validates `[MinBet, MaxBet]`, rejects self-challenge, checks the challenger balance, rejects duplicate pending challenges between the same two users in the same chat, inserts a `Pending` row, sends the inline keyboard.
2. Only the target can accept/decline. Decline → `Declined`. Accept atomically transitions `Pending → Accepted`.
3. On accept, both players are ensured in the chat wallet and debited with reason `challenge.stake`. If the second debit fails, the first stake is refunded and the row becomes `Failed`.
4. The handler resolves the game. Telegram-dice modes send two `SendDice` messages and compare their numeric values. Horse and blackjack take dedicated paths described below.
5. `CompleteAcceptedAsync`: equal scores → refund both stakes with `challenge.tie_refund`; otherwise `pot − fee` to the winner with `challenge.payout`.
6. Any exception after accept calls `FailAcceptedAsync` → refund both stakes + mark `Failed`.

**Fee math** (basis points, integer):

```
fee    = clamp(HouseFeeBasisPoints, 0, 10000) × (amount × 2) / 10000
payout = (amount × 2) − fee
```

Tiny pots can produce `fee = 0` because of integer truncation (e.g. 10-vs-10 at 2% rounds the fee from `0.4` to `0`).

**Horse challenges**: bypass Telegram dice. `SpeedGenerator.GenPlaces(2)` picks the winner; `HorseRaceRenderer.DrawHorses` renders a 2-horse GIF; uploaded as `challenge-horse.gif`. The settlement message is delayed by `frameCount × 60ms + 1s` so the text result doesn't spoil the animation. Scores are artificial (`2` for winner, `1` for loser) so the shared settlement method can still compute a winner.

**Blackjack challenges** are intentionally **instant auto-play duels**, not a turn-based table. Reuse `Games.Blackjack.Domain.Deck` and `BlackjackHandValue`:

- One crypto-shuffled 52-card deck.
- Challenger draws 2, auto-hits to ≥17.
- Target draws 2 from the same deck, auto-hits to ≥17.
- Score = `0` for bust, `22` for natural blackjack, otherwise hand total.
- Equal scores = push, both stakes returned.
- Cards rendered with friendly suit symbols (`Q♥ K♠ 10♦` …).

**Callback data**: `ch:a:<challengeIdN>` for accept, `ch:d:<challengeIdN>` for decline (UUID in compact `N` format).

**Analytics events**: `challenges.created`, `challenges.accepted`, `challenges.declined`, `challenges.tie`, `challenges.completed`, `challenges.failed_refunded` with tags for challenge id, chat id, both user ids, amount, game, scores, winner, payout, fee.

Config (section `Games:challenges`):

| Key | Default | Notes |
|---|---:|---|
| `MinBet`, `MaxBet` | `1`, `5000` | Stake bounds |
| `HouseFeeBasisPoints` | `200` | 2% of pot |
| `PendingTtlMinutes` | `10` | Clamped to 1..60 by `ChallengeOptions.PendingTtl` |

### Transfer (`Games.Transfer`, `/transfer`)

Lets a user send coins to another user **in the same group/supergroup wallet** (`balance_scope_id` = that chat's `Chat.Id`). **Not available in private DMs** (no shared recipient context).

**Semantics**:

- The **last whitespace-separated token** in the message is the **net amount the recipient receives**.
- **Fee** is computed on that net from `Games:transfer`: `FeePercent × net`, rounded to nearest 0.5, then to a whole coin (`MidpointRounding.AwayFromZero`), then clamped to at least `MinFeeCoins`. Optional `MinNetCoins`, `MaxNetCoins` (`0` = no cap).
- Sender debited `net + fee` with reason `transfer.send`; recipient credited `net` with `transfer.receive`. The fee is **not** credited anywhere (coins leave circulation).

**Atomicity**. `IEconomicsService.TryPeerTransferAsync` does it in one transaction: `SELECT … FOR UPDATE` on **both** `(user, scope)` rows in **ascending `telegram_user_id`** order (deadlock-safe), then two ledger inserts.

**Recipient resolution** (`TransferHandler`) — explicit recipient first, then reply:

1. `TextMention` entity (inline user picker).
2. `Mention` entity (`@username`) — distinct from `TextMention`. Mentions fully contained in a normal `BotCommand` span (e.g. `/transfer@YourBot`) are ignored, but a `BotCommand` span covering the **entire message** is **not** used to hide mentions.
3. Whitespace-split args (any Unicode whitespace, not only ASCII space — guards against NBSP gluing tokens).
4. Text between command and amount: numeric Telegram user id, or `@username` / handle resolved via `getChat` (private user only). Transfers to `Bot:Username` / `Bot:BotUsername` are rejected.
5. Reply to the target user's message — only if none of the above matched.

Success message shows sender + recipient balances after the transfer.

Config (section `Games:transfer`):

| Key | Default |
|---|---:|
| `FeePercent` | `0.03` |
| `MinFeeCoins` | `1` |
| `MinNetCoins` | `1` |
| `MaxNetCoins` | `0` (no cap) |

### PixelBattle (`Games.PixelBattle`, `/pixelbattle`)

Static assets live under `host/CasinoShiz.Host/wwwroot/pixelbattle`. `/pixelbattle` and `/pixelbattle/` redirect to `/pixelbattle/index.html`, served by ASP.NET static-file middleware.

**Telegram entry point**. `/pixelbattle` sends a Telegram WebApp button. `Games:pixelbattle:WebAppUrl` must be a public HTTPS URL, e.g. `https://your-public-host/pixelbattle/index.html`. Telegram WebApps do not work from private LAN URLs for other users; expose the bot with a tunnel (Cloudflare Tunnel, ngrok) for local testing. Stale message buttons may keep old tunnel URLs and stale Telegram init data — re-send `/pixelbattle` after URL changes.

**Auth**. API writes require Telegram WebApp `initData` in the `X-Telegram-Init-Data` header. `TelegramWebAppInitDataValidator` verifies the HMAC against the bot token and enforces `Games:pixelbattle:MaxInitDataAgeSeconds` (default `86400`, 24 h). Invalid/expired init data → `401`. Valid init data still must belong to a known user in the bot DB; unknown users → `403`.

**Grid model**:

- 200 × 160 pixels = 32 000 logical tiles.
- Colors restricted to the configured palette in `PixelBattleConstants`.
- `pixelbattle_tiles.index` = primary key.
- Each update stores `color`, updater Telegram user id, updated timestamp, and a monotonically increasing `version` from `pixelbattle_version_seq`.
- `PixelBattleGrid` returns parallel `Tiles` and `Versionstamps` arrays.
- `PixelBattleUpdate` = `{ index, color, versionstamp }`.

**Frontend rendering**: a single `<canvas>` (originally one DOM element per tile, replaced for perf). The JS keeps `tiles` and `versionstamps` arrays in memory, draws each tile with `drawPixel`, and maps click coordinates back to a tile index.

**Live updates** (SSE on `/pixelbattle/api/listen`):

1. Writes `retry: 1000`.
2. Streams a full-grid update immediately.
3. Subscribes to in-process `PixelBattleBroadcaster` and writes small update arrays as users paint.
4. Periodically sends a full grid again based on `Games:pixelbattle:FullUpdateIntervalMs` so clients self-heal if a small update is missed.

The update endpoint (`POST /pixelbattle/api/update`) validates init data, body shape, tile index, color, and known-user status, persists the tile, broadcasts the update, and returns the new versionstamp.

Config (section `Games:pixelbattle`):

| Key | Default | Notes |
|---|---:|---|
| `WebAppUrl` | empty | Public HTTPS WebApp URL. Must point to `/pixelbattle/index.html` in practice |
| `FullUpdateIntervalMs` | `10000` | Periodic full-grid SSE refresh |
| `MaxInitDataAgeSeconds` | `86400` | Maximum age for init data (`401` after) |

### Redeem (`Games.Redeem`, `/redeem` / `/codegen`)

Redeem code = single-use UUID that grants **one extra Telegram-dice roll** (not coins) for a specific game.

| Command | Effect |
|---|---|
| `/redeem <uuid>` | Activate a code. **Private chat only.** Triggers an emoji captcha. |
| `/codegen [count]` | Admin-only: generate copy-paste-ready codes |

**Drop flow**. After every Telegram-dice game roll, `TelegramMiniGameRedeemDrops.MaybePublishAsync` rolls against `Games:<game>:RedeemDropChance` (default `0.02`). On hit it publishes a `RedeemDropRequested` domain event consumed by `RedeemDropSubscriber`, which inserts a fresh `redeem_codes` row with `issued_by = 0`.

**Captcha**. On `/redeem`, the bot:

1. Validates UUID format and that the code is `active`.
2. Rejects redemptions of codes the user issued (`issued_by != userId` required; bot-issued drops use `IssuedBy = 0`, so dropper restrictions don't apply to drops).
3. Sends a 6-emoji captcha: pick the matching one within `CaptchaTimeoutMs` (default 15 s).
4. On correct answer: marks the code redeemed (`active = false`, `redeemed_by`), calls `IRollGate.GrantExtraRollAsync` to bump the daily roll quota by 1 for the matching `FreeSpinGameId`, posts confirmation.
5. On wrong answer or timeout: keeps the code active, friendly message.

**Captcha-timeout race fix**. `RedeemCaptchaTimeouts` (singleton) holds a `CancellationTokenSource` per pending code. When the user picks an answer, `RedeemHandler` calls `timeouts.TryCancel(codeGuid)` to atomically yank the timeout task before it fires. The fire-and-forget `Task.Delay` listens to that token and atomically `Forget`s its slot before sending the timeout message — so a successful redeem never races into a stray "you took too long" reply.

Config (section `Games:redeem`):

| Key | Default | Notes |
|---|---:|---|
| `FreeSpinGameId` | `dice` | Default game id for codegen / drops without explicit override |
| `CaptchaItems` | `6` | Number of emoji buttons in the captcha |
| `CaptchaTimeoutMs` | `15000` | Captcha timeout in ms |
| `MaxCodegenCount` | `20` | Hard cap on `/codegen` per command |
| `Admins` | empty | Extra Telegram user ids allowed to run `/codegen`; unioned with `Bot:Admins` |

### Leaderboard / wallet commands (`Games.Leaderboard`)

Five user-facing commands, plus one admin-only:

| Command | Restriction | Effect |
|---|---|---|
| `/balance` | none | Show this chat's wallet for the caller |
| `/daily` | none | Claim once-per-local-day bonus (see "Daily bonus" above) |
| `/top [full]` | none | Top players in this chat, with `DaysOfInactivityToHide` filter; `full` removes the trim |
| `/topall [full]` / `/topall split` | **admin + private DM only** | Cross-chat top. Default = aggregate (sum of coins across chats per user). `split` = per-chat breakdown. `full` removes inactivity filter. Output is chunked across multiple messages if it exceeds Telegram's 4096-char limit. |
| `/help` | none | Command reference (HTML), pulls dynamic config values for default bets, ticket price, draw hour, and timezone offset so the help text never goes stale |

Config (section `Games:leaderboard`):

| Key | Default | Notes |
|---|---:|---|
| `DefaultLimit` | `15` | How many rows to show by default |
| `DaysOfInactivityToHide` | `7` | Filters out users with no recent activity unless `full` is passed |

### Admin Telegram commands (`Games.Admin`)

Restricted to `Bot:Admins` (super-admin) or `Games:admin:Admins` (per-module list).

| Command | Restriction | Effect |
|---|---|---|
| `/run pay <id> <amount>` | admin | Manual coin adjustment (positive or negative); ledger reason `admin.adjust` |
| `/run userinfo` | admin | Reply to a message → returns Telegram user id |
| `/run cancel_blackjack <id>` | admin | Refund and remove a stuck blackjack hand |
| `/run kick_poker <id>` | admin | Remove a player from a poker table and refund their stack |
| `/run debug` | admin | Misc debug helpers |
| `/rename <old> <new\|*>` | admin | Display-name override; `*` clears |
| `/chats [filter] [full]` | **admin + private DM only** | List all chats the bot is in. Filter by chat type (`group`, `super`, `private`, `channel`); `full` removes truncation. Each row shows chat type, title/handle, id, user count, total coins, and last seen |
| `/analytics [window]` | **admin + private DM only** | Pull ClickHouse analytics: event counts, distinct users, top event types/modules/users for windows 24h / 7d / 30d, plus a daily timeline. Service: `ClickHouseAnalyticsQueryService` with parameterised, safe SQL |
| `/codegen [count]` | redeem-admin | Generate copy-paste `/redeem <uuid>` freespin codes. Capped by `Games:redeem:MaxCodegenCount` |
| `/horserun [global\|all]` | admin | Run today's horse race. In groups: this chat's pool only; in private: global by default |

## Migrations

Per-module, Dapper-based, tracked in `__module_migrations` under a `module_id` key. Applied at startup by `ModuleMigrationRunner` (registered before `BotHostedService` so schema is ready before polling starts).

Adding a migration = one new `Migration("name", "SQL")` entry in the module's `IModuleMigrations.GetMigrations()` list. No EF tooling.

### Framework migrations (`_framework`)

| ID | Contents |
|---|---|
| `001_event_store` | `module_events` + indexes |
| `002_snapshots` | `module_snapshots` |
| `003_users` | legacy single-scope `users` (superseded by `006`) |
| `004_event_log` | `event_log` |
| `005_admin_audit` | `admin_audit` |
| `006_per_chat_wallets_and_ledger` | `users` with `(telegram_user_id, balance_scope_id)` PK; `economics_ledger` |
| `007_known_chats` | `known_chats` (first/last seen per chat) |
| `008_users_last_daily_bonus` | `users.last_daily_bonus_on DATE` for `/daily` |
| `009_users_telegram_dice_daily` | `users.telegram_dice_rolls_on`, `telegram_dice_roll_count` — legacy single-counter daily cap (superseded by `012`) |
| `010_runtime_tuning` | `runtime_tuning` — JSON patch merged over file/env for whitelisted `Bot` + `Games` keys; edited from `/admin/settings` |
| `011_delivery_and_coordination` | `processed_updates`, `game_command_idempotency`, `mini_game_sessions`, `mini_game_roll_gates` |
| `012_telegram_dice_daily_per_game` | `telegram_dice_daily_rolls` with `(telegram_user_id, balance_scope_id, game_id)` PK for per-game daily caps |

### Module migrations

| Module | ID | Contents |
|---|---|---|
| `challenges` | `001_initial` | `challenge_duels` plus indexes by `(chat_id, status, created_at)` and `(target_id, status, expires_at)` |
| `pixelbattle` | `001_initial` | `pixelbattle_version_seq` + `pixelbattle_tiles` (each row stores `index`, `color`, `version`, `updated_by`, `updated_at`) |
| `pick` | `001_lottery` | `pick_lottery` (one open pool per chat via partial unique index) + `pick_lottery_entries` (PK `(lottery_id, user_id)`) + sweeper index on `deadline_at WHERE status = 'open'` |
| `pick` | `002_daily_lottery` | `pick_daily_lottery` (unique `(chat_id, day_local)`) + `pick_daily_lottery_tickets` (`BIGSERIAL` PK, one row per ticket) + sweeper / history indexes |

ClickHouse migrations are not run by the bot — they live under `db/clickhouse/` and are applied manually (currently `001_rename_analytics_to_cazinoshiz.sql`, used during the `analytics` → `cazinoshiz` database rename).

## Database schema

All game/balance persistence is PostgreSQL + Dapper. EF Core packages and a generic `EfRepository<T>` exist for optional module-owned repositories, but the shipped game hot paths and framework tables documented below are all Dapper-backed.

### Framework tables

#### `users`

| Column | Type | Notes |
|---|---|---|
| `telegram_user_id` | BIGINT | composite PK with `balance_scope_id` |
| `balance_scope_id` | BIGINT | usually `Chat.Id` — separate wallet per chat |
| `display_name` | TEXT NOT NULL | |
| `coins` | INTEGER NOT NULL DEFAULT 0 | written only through `EconomicsService` |
| `version` | BIGINT NOT NULL DEFAULT 0 | bumped on every balance mutation |
| `last_daily_bonus_on` | DATE NULL | last `/daily` claim in configured offset "day" |
| `telegram_dice_rolls_on` | DATE NULL | legacy single-counter (superseded by `telegram_dice_daily_rolls`) |
| `telegram_dice_roll_count` | INTEGER NULL | legacy single-counter |
| `created_at` / `updated_at` | TIMESTAMPTZ | |

#### `economics_ledger`

Append-only audit of balance changes: `delta`, `balance_after`, `reason` (e.g. `dice.stake`, `daily.bonus`, `pick.lottery.payout`, `admin.adjust`). See migration `006`.

#### `admin_audit`

| Column | Type | Notes |
|---|---|---|
| `id` | BIGSERIAL | PK |
| `actor_id` | BIGINT NOT NULL | Telegram user id of admin |
| `actor_name` | TEXT NOT NULL | |
| `action` | TEXT NOT NULL | e.g. `users.set_coins`, `runtime_tuning.pick.save` |
| `details` | JSONB NOT NULL DEFAULT '{}' | action-specific context |
| `occurred_at` | TIMESTAMPTZ | |

#### `known_chats`

Stores chats seen by the update pipeline. Admin pages use it to label chat ids with chat type, title, or username. The `1v1` admin page joins `challenge_duels.chat_id` here for readable chat labels. The `/chats` Telegram command uses it for the bot-side chat list.

#### `runtime_tuning`

Single-row `(id = 1)` JSONB overlay merged over `appsettings` / env on every read via `IRuntimeTuningAccessor`. `RuntimeTuningPayloadSanitizer` whitelists which keys may be stored — `Bot.{DailyBonus, TelegramDiceDailyLimit}` plus `Games.{dice, dicecube, darts, football, basketball, bowling, horse, poker, blackjack, sh, pick, challenges, transfer, pixelbattle, redeem, leaderboard, admin}`. Edited from `/admin/settings`.

#### `telegram_dice_daily_rolls`

Per-game daily roll counter for the Telegram-dice family.

| Column | Type | Notes |
|---|---|---|
| `telegram_user_id` | BIGINT | part of composite PK |
| `balance_scope_id` | BIGINT | wallet scope |
| `game_id` | TEXT | `dice`, `dicecube`, `darts`, `football`, `basketball`, `bowling` |
| `rolls_on` | DATE | last day on which the counter was bumped |
| `roll_count` | INTEGER | resets to `0` when `rolls_on` rolls over |
| `updated_at` | TIMESTAMPTZ | |

#### Other framework tables

`module_events`, `module_snapshots` (event-sourcing scaffolding), `event_log`, `processed_updates`, `game_command_idempotency`, `mini_game_sessions`, `mini_game_roll_gates`. See `FrameworkMigrations.cs` for full DDL.

### Game tables

#### `dicecube_bets` / `darts_bets` / `basketball_bets` / `bowling_bets`

Pending bet state for two-step dice games. Identical shape:

| Column | Type |
|---|---|
| `user_id` | BIGINT (PK) |
| `chat_id` | BIGINT (PK) |
| `amount` | INTEGER |
| `created_at` | TIMESTAMPTZ |

`dicecube_bets` additionally snapshots `mult4`/`mult5`/`mult6` at bet time so a runtime tuning change does not change the payout for a roll already in flight.

#### `blackjack_hands`

| Column | Type | Notes |
|---|---|---|
| `user_id` | BIGINT | PK |
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

See `games/Games.SecretHitler/` migrations. Deck serialized as `L` / `F` strings. Key columns: `status`, `phase`, `deck_state`, `discard_state`, `election_tracker`, `last_action_at`.

#### `horse_bets` / `horse_results`

Day-scoped on `race_date` (`MM-dd-yyyy` in `Games:horse:TimezoneOffsetHours`). Bets include `balance_scope_id` (Telegram chat). Results are keyed on `(race_date, balance_scope_id)`; scope `0` is the **global** merged race (all chats), other scopes are per-group races. `file_id` stores the Telegram animation reference.

#### `challenge_duels`

Owned by `Games.Challenges`. One row per 1v1 PvP challenge.

| Column | Type | Notes |
|---|---|---|
| `id` | UUID | PK, embedded in callback data as compact `N` format |
| `chat_id` | BIGINT | Chat/wallet scope where the challenge was created and settled |
| `challenger_id` / `target_id` | BIGINT | Telegram user ids |
| `challenger_name` / `target_name` | TEXT | Display names captured at challenge creation |
| `amount` | INTEGER | Per-player stake; pot is `amount × 2` |
| `game` | TEXT | Serialized `ChallengeGame` enum name |
| `status` | TEXT | `Pending`, `Accepted`, `Declined`, `Completed`, `Failed` |
| `created_at` | TIMESTAMPTZ | |
| `expires_at` | TIMESTAMPTZ | Pending challenges cannot be accepted after this time |
| `responded_at` | TIMESTAMPTZ NULL | First accept/decline/failure transition |
| `completed_at` | TIMESTAMPTZ NULL | Set on `Completed`, `Failed`, or `Declined` |

Indexes: `(chat_id, status, created_at DESC)` for admin/chat tracking; `(target_id, status, expires_at)` for target-side pending lookup.

#### `pick_lottery` / `pick_lottery_entries`

Owned by `Games.Pick`.

`pick_lottery`:

| Column | Type | Notes |
|---|---|---|
| `id` | UUID | PK |
| `chat_id` | BIGINT | |
| `opener_id` / `opener_name` | BIGINT / TEXT | |
| `stake` | INTEGER | Fixed stake for every entrant |
| `status` | TEXT | `open`, `settled`, `cancelled` |
| `opened_at` / `deadline_at` / `settled_at` | TIMESTAMPTZ | |
| `winner_id` / `winner_name` | BIGINT / TEXT | NULL until settled |
| `pot_total` / `payout` / `fee` | INTEGER | Snapshot of settlement math |

Indexes:

- `ux_pick_lottery_open_per_chat` — `UNIQUE (chat_id) WHERE status = 'open'`. One open pool per chat at most.
- `ix_pick_lottery_deadline_open` — `(deadline_at) WHERE status = 'open'`. Sweeper hot path.

`pick_lottery_entries`:

| Column | Type | Notes |
|---|---|---|
| `lottery_id` | UUID | FK → `pick_lottery(id) ON DELETE CASCADE`, part of PK |
| `user_id` | BIGINT | part of PK |
| `display_name` | TEXT | |
| `stake_paid` | INTEGER | What the user was actually debited (the pool stake at the time of entry) |
| `entered_at` | TIMESTAMPTZ | |

#### `pick_daily_lottery` / `pick_daily_lottery_tickets`

Owned by `Games.Pick`.

`pick_daily_lottery`:

| Column | Type | Notes |
|---|---|---|
| `id` | UUID | PK |
| `chat_id` | BIGINT | |
| `day_local` | DATE | Calendar date of the **draw** in the configured local offset |
| `ticket_price` | INTEGER | Snapshot of the ticket price at pool creation |
| `status` | TEXT | `open`, `settled`, `cancelled` |
| `opened_at` / `deadline_at` / `settled_at` | TIMESTAMPTZ | |
| `winner_id` / `winner_name` | BIGINT / TEXT | NULL until settled |
| `ticket_count` / `pot_total` / `payout` / `fee` | INTEGER | Snapshot of settlement math |

Indexes:

- `ux_pick_daily_lottery_chat_day` — `UNIQUE (chat_id, day_local)`. One pool per chat per day; serves `GetOrCreate` race-safely via `ON CONFLICT`.
- `ix_pick_daily_lottery_deadline_open` — sweeper hot path.
- `ix_pick_daily_lottery_chat_settled` — `(chat_id, day_local DESC) WHERE status = 'settled'` for `/dailylottery history`.

`pick_daily_lottery_tickets` — one row per ticket (so `ORDER BY random() LIMIT 1` is naturally weighted by ticket count):

| Column | Type | Notes |
|---|---|---|
| `id` | BIGSERIAL | PK |
| `lottery_id` | UUID | FK → `pick_daily_lottery(id) ON DELETE CASCADE` |
| `user_id` | BIGINT | |
| `display_name` | TEXT | |
| `price_paid` | INTEGER | What the user paid for this specific ticket |
| `bought_at` | TIMESTAMPTZ | |

Index: `(lottery_id, user_id)` for "how many tickets does X have in pool Y" lookups.

#### `pixelbattle_tiles`

Owned by `Games.PixelBattle`. Sparse table for painted tiles; missing rows render as the default color.

| Column | Type | Notes |
|---|---|---|
| `index` | INTEGER | PK; flattened index in a 200 × 160 grid |
| `color` | TEXT | Hex color from the allowed palette |
| `version` | BIGINT | Monotonic version from `pixelbattle_version_seq` |
| `updated_by` | BIGINT | Telegram user id from validated WebApp init data |
| `updated_at` | TIMESTAMPTZ | |

#### `redeem_codes`

| Column | Type | Notes |
|---|---|---|
| `code` | UUID | PK |
| `active` | BOOLEAN | `false` once redeemed |
| `issued_by` | BIGINT | `0` for bot drops; user id for `/codegen` |
| `free_spin_game_id` | TEXT | Same-game extra roll target (`dice`, `darts`, `bowling`, …) |
| `redeemed_by` | BIGINT NULL | |
| `issued_at` / `redeemed_at` | TIMESTAMPTZ | |

## Admin web UI

Razor pages under `/admin/*` served on the same port as the webhook (default `3000`).

### Authentication

`GET /admin/login` — two sign-in methods:

1. **Telegram Login Widget** — HMAC-SHA256 with `SHA256(botToken)` as key, 24 h freshness check. Requires `Bot:Username` and the bot domain registered in BotFather. Real domains only — does not work on localhost.
2. **Token form** — constant-time comparison against `Bot:AdminWebToken`. Works everywhere; used for local dev.

Successful auth writes an `AdminSession` (`UserId`, `Name`, `Role`) to `ISession`. Gate middleware redirects unauthenticated `/admin/*` to `/admin/login`.

### RBAC

| Role | Source | Permissions |
|---|---|---|
| `SuperAdmin` | `Bot:Admins` | Full access — mutate balances, run races, edit runtime tuning |
| `ReadOnly` | `Bot:ReadOnlyAdmins` | View only — mutation endpoints return 403 |

### Audit log

Every write action from the admin UI is recorded in `admin_audit`:

| Column | Description |
|---|---|
| `actor_id` / `actor_name` | Telegram user from session |
| `action` | e.g. `users.set_coins`, `horse.run_race`, `runtime_tuning.pick.save` |
| `details` | JSONB with action-specific context |
| `occurred_at` | TIMESTAMPTZ |

### Pages

- `/admin` — dashboard: stats, user search
- `/admin/users` — user list, balance set/adjust (SuperAdmin only, via `IEconomicsService.AdjustUncheckedAsync`)
- `/admin/people` — merged person view across known chats/scopes
- `/admin/groups` — known chat list
- `/admin/ledger` — economics ledger search/recovery
- `/admin/horse` — race control panel: today's bets, koefs, "Run race" button (SuperAdmin only)
- `/admin/horse/image` — horse race image/GIF preview
- `/admin/bets` — pending bets
- `/admin/challenges` — 1v1 PvP challenge tracking (read-only, both roles)
- `/admin/history` — race history
- `/admin/events` — event log
- `/admin/settings` — live runtime JSON patch editor + structured forms (SuperAdmin only)

### `/admin/settings`

The page combines a **live preview** (read-only effective values), a **JSON patch editor** for arbitrary whitelisted keys, and **structured forms** for the most-tuned subsets:

- **Sticker game testing** — `RedeemDropChance` and per-game daily roll caps for all six Telegram-dice games
- **Poker** — `BuyIn`
- **Blackjack** — `MinBet`, `MaxBet`, `HandTimeoutMs`
- **Secret Hitler** — `BuyIn`
- **Challenge / PvP** — `MinBet`, `MaxBet`, `HouseFeeBasisPoints`, `PendingTtlMinutes`
- **Pick / lotteries** — full set: `DefaultBet`, `MaxBet`, `HouseEdge`, streak (`StreakBonusPerWin`, `StreakCap`), chain (`ChainMaxDepth`, `ChainTtlSeconds`), `RevealAnimation`; lottery (`DurationSeconds`, `MinEntrantsToSettle`, `HouseFeePercent`, `MinStake`, `MaxStake`); daily lottery (`TicketPrice`, `MaxTicketsPerUserPerDay`, `MaxTicketsPerBuyCommand`, `HouseFeePercent`, `HistoryLimit`, `DrawHourLocal`, `TimezoneOffsetHoursOverride`)

The save flow for every form is the same: `RuntimeTuningPayloadSanitizer.Sanitize` → `RuntimeTuningMerge.MergeSection<T>` for type-level validation → write to `runtime_tuning.payload` → `IRuntimeTuningAccessor.ReloadFromDatabaseAsync` → audit log entry → reload preview. The free-form **JSON patch** path runs through the same sanitize + validate steps.

Pending 🎲 cube bets snapshot their `mult4/5/6` at bet time, so a tuning change does not change payouts for already-thrown rolls.

### `/admin/challenges` (1v1)

Host-owned Razor UI, backed by direct Dapper queries over `challenge_duels` and `known_chats`. Read-only, available to both roles through the normal admin gate.

Filters: game (all 8 challenge enums), status (`Pending`, `Accepted`, `Completed`, `Declined`, `Failed`), chat (distinct chats from `challenge_duels`, labeled from `known_chats`).

Summary cards: total duels for the current filter; pending / accepted / completed / cancelled counts; total pot tracked (`sum(amount × 2)`).

Tables: by game type (count, pending, completed, total pot, last created); by chat (label/id, count, pending/completed, total pot, last created); recent duels (newest 500 with full row details).

Intended for operational questions: "which chat uses PvP most?", "which game type is popular?", "are there stuck accepted challenges?", "how much pot volume is flowing through 1v1?".

## Configuration

`appsettings.json` is the source of truth; env vars override (Docker: `environment` block + `.env` file; K8s: Secret-backed env). The `runtime_tuning` DB row merges on top — see "Runtime tuning (admin overlay)" above.

### `Bot` section

| Key | Required | Description |
|---|---|---|
| `Token` | yes | Telegram bot API token |
| `Username` / `BotUsername` | yes | Bot @username (with or without @); `BotUsername` is accepted as a config alias and used in `appsettings.json` |
| `Admins` | yes | Telegram user IDs with SuperAdmin access |
| `ReadOnlyAdmins` | no | Telegram user IDs with ReadOnly access |
| `AdminWebToken` | no | Token for password-based admin login |
| `IsProduction` | no | `true` → webhook; `false` → polling (default) |
| `WebhookPort` | no | Kestrel port in webhook mode (default `3000`) |
| `TrustedChannel` | no | @username for race GIF broadcast |
| `StartingCoins` | no | Coins for new users (default `100`) |
| `DailyBonus` (nested) | no | `Enabled`, `PercentOfBalance` (% of balance, e.g. `0.35` = **0.35 %** because the formula divides by 100 internally), `MaxBonus`, `TimezoneOffsetHours` |
| `TelegramDiceDailyLimit` (nested) | no | `MaxRollsPerUserPerDayByGame` maps `dice`, `dicecube`, `darts`, `football`, `basketball`, `bowling` to per-day caps (`0` = unlimited for that game); `MaxRollsPerUserPerDay` is only a fallback for missing game ids; `TimezoneOffsetHours` follows `DailyBonus`. Cap is per wallet (chat scope). |

### `Games` section

| Key | Description |
|---|---|
| `dice:Cost` | 🎰 slot spin stake (gas tax adds 0 at `Cost = 10`) |
| `<game>:RedeemDropChance` | Chance per resolved sticker-game roll/throw to drop a `/redeem <uuid>` code for that same game (`0.02` = 2%) |
| `dicecube:Mult4` / `Mult5` / `Mult6` | Pay multipliers for faces 4–6 on `/dice` 🎲 |
| `dicecube:MaxBet`, `MinSecondsBetweenBets` | Stake cap and per-chat cooldown |
| `darts:*`, `bowling:*`, `football:*`, `basketball:*` | `MaxBet`, `DefaultBet`, `RedeemDropChance` |
| `horse:*` | See below |
| `poker:BuyIn` | Buy-in per seat |
| `blackjack:MinBet`, `MaxBet`, `HandTimeoutMs` | |
| `sh:BuyIn` | Secret Hitler buy-in |
| `pick:*` | See below |
| `challenges:*` | See below |
| `transfer:*` | `FeePercent`, `MinFeeCoins`, `MinNetCoins`, `MaxNetCoins` |
| `pixelbattle:*` | See below |
| `redeem:*` | `FreeSpinGameId`, `CaptchaItems`, `CaptchaTimeoutMs`, `MaxCodegenCount`, `Admins` |
| `leaderboard:*` | `DefaultLimit`, `DaysOfInactivityToHide` |
| `admin:Admins` | Per-module admin list (unioned with `Bot:Admins`) |

`Games:horse` — `HorseCount`, `MinBetsToRun`, `AnnounceDelayMs`, `AnnounceDelay1v1Ms` (used when `HorseCount` is 2, longer delay so the winner message does not appear before the GIF finishes), `TimezoneOffsetHours`, `Admins` (Telegram user IDs allowed to `/horserun` in addition to `Bot:Admins`), `AutoRunEnabled`, `AutoRunLocalHour`, `AutoRunLocalMinute`. When `AutoRunEnabled` is true, `HorseScheduledRaceJob` runs **one global** race per calendar day after the configured local time, if there are enough bets (`MinBetsToRun`). It settles like `/horserun global` and posts the result GIF only to chats that placed bets.

`Games:challenges`:

| Key | Default | Notes |
|---|---:|---|
| `MinBet` | `1` | Rejects smaller stakes |
| `MaxBet` | `5000` | Rejects larger stakes |
| `HouseFeeBasisPoints` | `200` | bps on the combined pot; `200` = 2%. Integer division truncates |
| `PendingTtlMinutes` | `10` | Pending expiry, clamped to 1..60 minutes |

`Games:pick` — see "Pick / Pick lottery / Daily lottery" sections above for the full key list (`DefaultBet`, `MaxBet`, `HouseEdge`, `StreakBonusPerWin`, `StreakCap`, `ChainMaxDepth`, `ChainTtlSeconds`, `RevealAnimation`, `RevealStepDelayMs`, `RevealMaxTotalMs`, plus nested `Lottery` and `Daily` blocks).

`Games:pixelbattle`:

| Key | Default | Notes |
|---|---:|---|
| `WebAppUrl` | empty | Public HTTPS Telegram WebApp URL. Must point to `/pixelbattle/index.html` |
| `FullUpdateIntervalMs` | `10000` | Periodic full-grid SSE snapshot interval |
| `MaxInitDataAgeSeconds` | `86400` | Maximum age of Telegram WebApp init data — expired returns `401` |

### Other sections

| Section | Key | Required when |
|---|---|---|
| `ConnectionStrings` | `Postgres` | always |
| `Redis` | `Enabled`, `ConnectionString` | `Redis:Enabled = true` |
| `ClickHouse` | `Enabled`, `Host`, `Database`, `Table`, `Project` | `ClickHouse:Enabled = true` (validation throws on startup if `Enabled` but `Host` is empty) |

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
| Prometheus | 9090 | `http://localhost:9090` | Exporter, cAdvisor, .NET runtime metrics |
| Grafana | 3001 | `http://localhost:3001` | Dashboards (admin/admin) |

`dotnet-monitor` runs as an internal compose service and exposes only its Prometheus metrics endpoint to the Docker network. The bot uses `DOTNET_DiagnosticPorts=/diag/dotnet-monitor.sock,nosuspend`, so metrics are collected when the monitor is available without blocking bot startup if the monitor is absent.

Compose also runs `postgres-exporter`, `redis-exporter`, `cadvisor`, and `dotnet-monitor` for Prometheus-backed Grafana dashboards. `db-backup` is a manual/one-shot helper that writes a `pg_dump` to the repository root when explicitly run.

Provisioned Grafana dashboards:

- `overview.json` — ClickHouse event overview
- `analytics-clickhouse.json` — product analytics from `events_v2`: overview stats, TOD/weekday, DAU, per-chat, stacked hourly users/events by game, pie mix, user intensity, per-user × game matrix, drill-down user (defaults to busiest in range), extended signals (distinct types/modules, chat-tagged rows, errors by `exception_type`, repeat-day users, event breadth, params keys, tail), **Economics & bot dynamics**, **Growth, quality & depth**, **Weekly leaderboards** (top 15 modules, users, tagged chats per ISO week UTC), **Retention, funnels & stake distribution** (cohort w0–w8, funnel conversion %, stake quantiles, histogram), plus **Chart views** (bottom row: bar / time series companions for most heavy **table** panels — event types, modules, users, errors, params keys, funnel steps, payouts, repeat users, house edge, step conversion %, weekly module stack, weekly actives & tagged-chat volume, cohort w1–w4 curves, daily median stake, multi-game bursts, daily rollup, top wagerers).
- `infra-pg-redis.json` — PostgreSQL and Redis exporter metrics
- `infra-services.json` — cAdvisor container CPU/memory + connection panels
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

Register the webhook with Telegram after deploy:

```
https://api.telegram.org/bot<TOKEN>/setWebhook?url=https://bot.example.com/<TOKEN>
```

## Analytics

`ClickHouseAnalyticsService` (singleton) buffers events and flushes every `FlushIntervalMs`. Creates its table on startup. If ClickHouse is unreachable at startup the service logs and disables itself — the bot never blocks on analytics.

Validation: if `ClickHouse:Enabled = true` but `ClickHouse:Host` is empty, startup throws immediately.

The default database/table/project is `cazinoshiz.events_v2` with `project = 'cazinoshiz'`. The legacy `analytics.events_v2` / `project = 'busino-bot'` schema was renamed via `db/clickhouse/001_rename_analytics_to_cazinoshiz.sql` (copy-then-drop, preserves history).

Raw query: `curl 'http://localhost:8123/?query=SELECT+*+FROM+cazinoshiz.events_v2+LIMIT+10'`

The `/analytics` Telegram command (admin-only, private DM only) wraps `ClickHouseAnalyticsQueryService` with parameterised, safe SQL — event counts, distinct users, top event types/modules/users for 24h / 7d / 30d windows, plus a daily timeline.

## Testing

680+ xUnit tests under `tests/CasinoShiz.Tests/`. No external database in CI — games use in-memory fakes (`FakeEconomicsService`, `InMemoryBlackjackHandStore`, etc.). `DailyBonusMath` unit-tests the bonus coin formula.

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~HandEvaluatorTests"
dotnet test --filter "DisplayName~majorityJa"
```

Coverage: domain logic (poker, secret hitler, blackjack, dice), services, taxes, PRNG, Russian plurals, router attribute scanning, `InProcessEventBus`, all framework contracts.

## Bot commands (full reference)

All UI in Russian. Command names are ASCII.

### Everyone

| Command | Effect |
|---|---|
| `🎰` | Slot machine — `Games:dice:Cost` + gas, fixed prize table, optional same-game redeem-code drop |
| `/dice bet <amount>` | Bot sends `🎲` after bet (reply to you); you can still send your own `🎲`. 4→×1, 5→×2, 6→×2 |
| `/darts bet <amount>` | Bot sends 🎯 or you throw. 4→×1, 5→×2, 6→×2 |
| `/football bet`, `/basket bet` | Bot sends ⚽/🏀 or you throw. 4→×2, 5→×2 |
| `/bowling bet` | Bot sends 🎳 or you throw. 4→×1, 5→×2, 6 (strike)→×2 |
| `/horse bet <1-N> <amount>` | Bet on today's race in this chat |
| `/horse info` | This chat's pool: stakes + koefs |
| `/horse result` | This chat's last race GIF, else today's global race |
| `/poker create / join / start / fold / call / raise <n> / check / leave` | Texas Hold'em |
| `/blackjack <bet>` | Start a hand; inline keyboard drives hit / stand / double |
| `/sh create / join / start / nominate <user> / vote yes\|no / leave` | Secret Hitler (5–10 players) |
| `/pick <variants…>` and variants (see `/pick help`) | Pick game with explicit choice / parlay / streak / ×2 chain |
| `/picklottery <stake>`, `/pickjoin`, `/picklottery info / cancel / help` | 5-min lottery in this chat |
| `/dailylottery [buy] [N]`, `/dailylottery history / help` | Daily lottery in this chat (draw at the configured local hour) |
| `/challenge @user <amount> <game>` | 1v1 PvP stake challenge |
| `/challenge <amount> <game>` | Same, when replying to the target user's message |
| `/pixelbattle` | Open the PixelBattle Telegram WebApp |
| `/transfer <target> <amount>` | Send coins (in groups only). Amount = recipient net |
| `/redeem <uuid>` | Redeem one extra roll for the code's game (private chat only, emoji captcha) |
| `/balance` | Current coin balance for this chat's wallet |
| `/daily` | Once per local day after offset: small % of balance, capped — see `Bot:DailyBonus` |
| `/top [full]` | Per-chat leaderboard |
| `/help` | Command reference (HTML), dynamic — pulls actual config values |

### Bot-admin only (`Bot:Admins`)

| Command | Restriction | Effect |
|---|---|---|
| `/horserun [global\|all]` | admin | Run today's race (group → this chat's pool; private → global by default) |
| `/codegen [count]` | redeem-admin | Generate copy-paste-ready `/redeem <uuid>` freespin codes |
| `/run pay <id> <amount>` | admin | Manual coin adjustment |
| `/run userinfo` | admin | Reply to a message → returns Telegram user id |
| `/run cancel_blackjack <id>` | admin | Refund and remove a stuck hand |
| `/run kick_poker <id>` | admin | Remove a player from a table and refund their stack |
| `/rename <old> <new\|*>` | admin | Display-name override; `*` clears |
| `/topall [full\|split]` | admin + private DM only | Cross-chat leaderboard (aggregated by default; `split` = per-chat breakdown) |
| `/chats [filter] [full]` | admin + private DM only | List bot chats, with type filter and optional full mode |
| `/analytics [window]` | admin + private DM only | ClickHouse summary (events / users / tops / timeline) |

## Conventions

- User strings: Russian, live in each module's `Locales.cs` — never inline.
- Plural forms: `RussianPlural.Plural(n, ["монета","монеты","монет"])`.
- Randomness: Telegram dice outcomes come from Telegram; blackjack/poker decks use `RandomNumberGenerator`; horse winner/place generation uses the horse generator path and renders deterministic speed series for the chosen places; `/pick` and pick lotteries use `Random.Shared` (sufficient for non-monetary fairness — the house edge / fee is the protective layer).
- No `Task.Delay` for scheduling — use `IBackgroundJob` / `IHostedService` sweepers.
- Services return result records; handlers map to messages. Only throw for programmer errors.
- **Balance changes go through `IEconomicsService` — always.** Never raw SQL on `users.coins`.
- Logging: source-generated `[LoggerMessage]` only. No string interpolation in log calls. Classes that use `[LoggerMessage]` must be `partial`.
- Primary constructors are the default style: `public sealed class Foo(Dep dep) : IBar`.
- Services: `Scoped`; hosted services / analytics / process-memory state stores (e.g. `PickStreakStore`, `PickChainStore`): `Singleton`; `ITelegramBotClient`: `Singleton`.
- Module migrations are append-only — never edit a shipped migration; add a new one.
- `Games:*` config values are tunable at runtime through `/admin/settings`. Hard limits and security-sensitive options stay in `appsettings.json` / env (e.g. tokens, admin lists).
