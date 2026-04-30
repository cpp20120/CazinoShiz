# CasinoShiz

[![](https://tokei.rs/b1/github/cpp20120/CazinoShiz)](https://github.com/cpp20120/CazinoShiz)

Telegram casino and party-game bot with Russian-language UI, wallet balances, admin tools, analytics, and a modular game system. It runs as an ASP.NET Core host with independent game modules.

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
| `/transfer` | Coin transfers between users, including transfer fees. |
| `/redeem` | Freespin/redeem code activation. |
| `/top`, `/balance`, `/daily`, `/help` | Leaderboard, wallet balance, daily bonus, and help. |

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
| Runtime | ASP.NET Core, .NET 10 preview SDK |
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

- Dashboard: loaded modules, wallet totals, event counts, sticker-game play counts.
- People, Wallets, Ledger, History: user and balance tracking.
- Chats: known group/private/channel list from Telegram updates.
- Bets: pending game bets across supported modules.
- 1v1: challenge tracking by game type, status, chat, players, stake, and pot.
- Horse: race controls, schedule visibility, and GIF preview tools.
- Events: recent domain events with module/chat filters.
- Settings: runtime configuration overview.

All write actions are logged to the `admin_audit` table.

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

Telegram dice games also support per-game `MaxBet`, `DefaultBet`, and redeem drop chance options under `Games:<game>`.

## Database And Migrations

Modules provide Dapper-based migrations through `IModuleMigrations`. The host applies them automatically at startup and tracks applied migrations in `__module_migrations`.

Important module tables include:

- `users`: wallet balances scoped by chat/user.
- `event_log`: domain events used by admin tracking.
- `challenge_duels`: 1v1 challenge state.
- `pixelbattle_tiles`: PixelBattle grid state.
- per-game pending tables such as `blackjack_hands`, `horse_bets`, and Telegram dice bet tables.

## Observability

When enabled, analytics events are buffered to ClickHouse and visualized in Grafana. The Docker stack also includes Prometheus targets for Postgres, Redis, and container metrics.

If Grafana panels are empty, check Prometheus targets first, then verify Grafana data sources. Old cached Grafana state may require recreating the stack or removing the `grafana_data` volume.

## License

[MIT](LICENSE)
