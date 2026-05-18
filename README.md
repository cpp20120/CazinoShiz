# CasinoShiz

[![](https://tokei.rs/b1/github/cpp20120/CazinoShiz)](https://github.com/cpp20120/CazinoShiz)

Telegram party-games bot with virtual credits and party-game bot with Russian-language UI, wallet balances, admin tools, analytics, and a modular game system. It runs as an ASP.NET Core host with independent game modules. 

DISCLAMER: THIS BOT IS HAS NO ANY REAL MONEY AND ABILITY TO USE THEM.

This project is a simulation-only system.

- It does not support real money, cryptocurrencies, or any form of monetary value.
- Virtual coins/credits:
  - cannot be purchased
  - cannot be sold
  - cannot be exchanged
  - cannot be transferred for any real-world goods, services, or value
- There is no mechanism to deposit or withdraw real money.
- Redeem codes are generated internally for gameplay purposes only and have no monetary value.

This software must not be used for real-money gambling or any commercial gambling activity.

The primary purpose of this project is educational, focusing on architecture patterns such as DDD, Event Sourcing, and CQRS.

## Features

### Telegram Games

| Command | Feature |
|---|---|
| `/dice` | Dice cube betting with Telegram 🎲 dice animation. |
| `/darts` | Darts betting with Telegram 🎯 dice animation. |
| `/football` | Football betting with Telegram ⚽ dice animation. |
| `/basket` | Basketball betting with Telegram 🏀 dice animation. |
| `/bowling` | Bowling betting with Telegram 🎳 dice animation. |
| `/blackjack` | Single-player blackjack with hit, stand, double, timeout cleanup, and persisted in-progress hands. |
| `/horse` | Horse-race betting with SkiaSharp GIF rendering, scheduled races, manual admin runs, and place labels in the animation. |
| `/poker` | Texas Hold'em poker tables. |
| `/sh` | Secret Hitler party game. |
| `/challenge` | 1v1 PvP betting challenges between two Telegram users. |
| `/pixelbattle` | Telegram WebApp mini app for shared pixel painting. |
| `/picklottery` | Per chat pool lottery                               |
| `/transfer` | Coin transfers between users, including transfer fees. |
| `/redeem` | Freespin/redeem code activation. |
| `/top`, `/balance`, `/daily`, `/help` | Leaderboard, wallet balance, daily bonus, and help. |

### Operations documentation

See [docs/operations.md](docs/operations.md) for the runtime runbook covering:

- daily bonus and downtime catch-up
- scheduled horse races
- `/__debug_jobs` and Admin Dashboard job visibility
- idempotent money-flow SQL checks
- admin wallet edits
- event dispatch failure retry commands
- Docker port mapping checks

### 1v1 Challenges

`/challenge` lets one player challenge another player to a PvP stake. The challenger can use a username:

```text
/challenge @username 500 dicecube
```

Or reply to a user's message:

```text
/challenge 500 darts
```

Supported challenge games:

- `dice` / `dicecube` 🎲
- `darts` 🎯
- `bowling` 🎳
- `basketball` 🏀
- `football` ⚽
- `slots` 🎰
- `horse` 🐎
- `blackjack` 🃏

Challenges escrow both players' stakes when accepted, return stakes on ties/failures, and pay the winner the pot minus the configured house fee. Horse challenges render a 2-player GIF and wait for the animation before announcing the winner. Blackjack challenges are auto-resolved with crypto-shuffled hands that draw until 17.

### PixelBattle WebApp

PixelBattle is a Telegram WebApp served from `wwwroot/pixelbattle` and opened by `/pixelbattle`. It uses:

- Telegram WebApp `initData` validation
- a canvas renderer for the pixel grid
- Server-Sent Events for live updates
- PostgreSQL persistence

Telegram requires a public HTTPS URL for WebApps. For local development, expose the host with a tunnel and set:

```text
Games__pixelbattle__WebAppUrl=https://your-public-host/pixelbattle/index.html
```

Old WebApp buttons can keep stale Telegram `initData`; send a fresh `/pixelbattle` command if a page starts returning `401`.

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 SDK |
| Telegram | `Telegram.Bot` 22.x, polling and webhook modes |
| Persistence | PostgreSQL 16 via Dapper |
| Event bus | In-process domain event bus, with Redis/CAP support where configured |
| Update fan-out | Redis Streams, opt-in and partitioned by chat id |
| Analytics | ClickHouse buffered writer, disabled gracefully when unavailable |
| Dashboards | Grafana, Prometheus, Postgres/Redis exporters, cAdvisor |
| Graphics | SkiaSharp 3.x for horse race GIF rendering |
| Tests | xUnit tests for framework, domain logic, services, and routing |

## Project Structure

```text
framework/
  BotFramework.Sdk/          module contracts: IModule, IUpdateHandler, route attrs, economics, analytics
  BotFramework.Sdk.Testing/  xUnit helpers for module tests
  BotFramework.Host/         ASP.NET host services, router, migrations, economics, analytics, admin auth
games/
  Games.Dice/ Games.DiceCube/ Games.Darts/ Games.Football/
  Games.Basketball/ Games.Bowling/ Games.Blackjack/ Games.Horse/
  Games.Poker/ Games.SecretHitler/ Games.Challenges/ Games.PixelBattle/
  Games.Redeem/ Games.Leaderboard/ Games.Transfer/ Games.Admin/
host/
  CasinoShiz.Host/           Program.cs composition root and Razor admin UI
tests/
  CasinoShiz.Tests/
```

Each module owns its handlers, options, migrations, locale strings, and services. The host wires modules through `AddBotFramework().AddModule<T>()`.

## Setup

Copy `.env.example` to `.env` and fill in required fields:

```bash
cp .env.example .env
# edit .env: set Bot__Token, Bot__Username, Bot__Admins__0, ConnectionStrings__Postgres
```

Run locally in polling mode:

```bash
dotnet build
dotnet run --project host/CasinoShiz.Host
```

Run with the full stack:

```bash
docker compose up --build
```

Useful local URLs:

- Bot/admin host: `http://localhost:3000`
- Admin login: `http://localhost:3000/admin/login`
- Grafana: `http://localhost:3001`
- Prometheus: `http://localhost:9090`

Run tests:

```bash
dotnet test
```

## Admin UI

Go to `http://localhost:3000/admin/login`.

Sign-in methods:

- Token form: paste `Bot__AdminWebToken` from `.env`.
- Telegram Login Widget: requires `Bot__Username` and BotFather `/setdomain`; public domains only.

Roles:

- `SuperAdmin` from `Bot__Admins`: full access, including balance mutations and manual race actions.
- `ReadOnly` from `Bot__ReadOnlyAdmins`: view-only; mutation endpoints return `403`.

Admin pages include:

- Dashboard: loaded modules, wallet totals, event counts, sticker-game play counts, and background/host job status.
- People, Wallets, Ledger, History: user and balance tracking.
- Chats: known group/private/channel list from Telegram updates.
- Bets: pending game bets across supported modules.
- 1v1: challenge tracking by game type, status, chat, players, stake, and pot.
- Horse: race controls, schedule visibility, and GIF preview tools.
- Events: recent domain events with module/chat filters.
- Settings: runtime configuration overview.

All write actions are logged to the `admin_audit` table. Admin wallet Set/+/- operations also write idempotent `economics_ledger.operation_id` values to protect against duplicate form submits.

## Diagnostics & Health

The host provides built-in endpoints and hidden commands for monitoring:

- **HTTP `/health/live`**: Liveness probe endpoint. Returns HTTP 200 (or 503) based on basic application responsiveness.
- **HTTP `/health/ready`**: Readiness probe endpoint. Returns HTTP 200 (or 503) along with the status of configured infrastructure dependencies (PostgreSQL, Redis, ClickHouse).
- **Telegram `/debug` command**: A hidden bot command that reports technical process metrics directly in the chat, including the current `chat_id`, `chat_type`, process `uptime`, total `cpu time`, and memory usage (`rss`).
- **Telegram `/__debug_jobs` command**: Shows module and host job status, including horse scheduled races and daily bonus catch-up.
- **Telegram `/__debug_dispatch_failures` command**: Lists unresolved projection/event dispatch failures.
- **Telegram `/__debug_retry_dispatch_failure <id>` command**: Retries one unresolved dispatch failure.

## Configuration

Most settings can be provided in `appsettings.json` or as environment variables with `__` separators.

| Key | Required | Description |
|---|---|---|
| `Bot__Token` | yes | Telegram bot API token. |
| `Bot__Username` | yes | Bot username, with or without `@`; used by Telegram login and links. |
| `Bot__Admins__0` | yes | Telegram user ID with full admin access. |
| `Bot__ReadOnlyAdmins__0` | no | Telegram user ID with read-only admin access. |
| `Bot__AdminWebToken` | no | Token for password-style admin login. |
| `Bot__IsProduction` | no | `true` for webhook mode, `false` for polling. |
| `Bot__WebhookBaseUrl` | production | Public webhook base URL. |
| `Bot__WebhookPort` | no | HTTP port used by the host. |
| `Bot__TrustedChannel` | no | Channel username for trusted broadcasts/race posting. |
| `Bot__StartingCoins` | no | Initial user balance. |
| `Bot__DailyBonus__Enabled` | no | Enables manual `/daily` and catch-up logic. |
| `Bot__DailyBonus__PercentOfBalance` | no | Percent of current balance credited by daily bonus. |
| `Bot__DailyBonus__MaxBonus` | no | Upper cap for daily bonus amount. |
| `Bot__DailyBonus__TimezoneOffsetHours` | no | Local-day boundary used by daily bonus and catch-up. |
| `Bot__DailyBonus__CatchUpEnabled` | no | Enables downtime catch-up for missed past local days. |
| `Bot__DailyBonus__MaxCatchUpDays` | no | Safety cap for number of days catch-up can process. |
| `ConnectionStrings__Postgres` | yes | PostgreSQL connection string. |
| `Redis__Enabled` | no | Enables Redis-backed infrastructure. |
| `Redis__ConnectionString` | if enabled | Redis connection string, for example `redis:6379`. |
| `ClickHouse__Enabled` | no | Enables analytics writes. |
| `ClickHouse__Host` | if enabled | ClickHouse HTTP endpoint. |
| `Games__pixelbattle__WebAppUrl` | PixelBattle | Public HTTPS URL ending in `/pixelbattle/index.html`. |
| `Games__challenges__MinBet` | no | Minimum 1v1 challenge stake. |
| `Games__challenges__MaxBet` | no | Maximum 1v1 challenge stake. |
| `Games__challenges__HouseFeeBasisPoints` | no | Challenge fee in basis points, where `200` is 2%. |
| `Games__challenges__PendingTtlMinutes` | no | Time before pending challenges expire. |
| `Games__transfer__FeePercent` | no | Transfer fee ratio. |
| `Games__horse__AutoRunEnabled` | no | Enables scheduled horse races. |
| `Games__horse__AutoRunLocalHour` / `Games__horse__AutoRunLocalMinute` | no | Local scheduled race time. |
| `Games__horse__TimezoneOffsetHours` | no | Local day/timezone for horse scheduled races. |

Telegram dice games also support per-game `MaxBet`, `DefaultBet`, and redeem drop chance options under `Games:<game>`.

See [docs/operations.md](docs/operations.md) for operation ids, SQL smoke checks, catch-up behavior, scheduled jobs, and Docker port mapping notes.

## Database And Migrations

Modules provide Dapper-based migrations through `IModuleMigrations`. The host applies them automatically at startup and tracks applied migrations in `__module_migrations`.

Important module tables include:

- `users`: wallet balances scoped by chat/user.
- `event_log`: domain events used by admin tracking.
- `module_events`: append-only Event Sourcing event streams.
- `event_dispatch_failures`: unresolved projection/dispatch failures with retry metadata.
- `challenge_duels`: 1v1 challenge state.
- `pixelbattle_tiles`: PixelBattle grid state.
- per-game pending tables such as `blackjack_hands`, `horse_bets`, and Telegram dice bet tables.

## Observability

When enabled, analytics events are buffered to ClickHouse and visualized in Grafana. The Docker stack also includes Prometheus targets for Postgres, Redis, and container metrics.

If Grafana panels are empty, check Prometheus targets first, then verify Grafana data sources. Old cached Grafana state may require recreating the stack or removing the `grafana_data` volume.

Operational visibility is available through:

- Admin Dashboard background/host jobs table.
- `/__debug_jobs` for Telegram-side job status.
- `/__debug_dispatch_failures` and `/__debug_retry_dispatch_failure <id>` for failed event dispatches.
- SQL checks in [docs/operations.md](docs/operations.md).

## License

[MIT](LICENSE)