# CasinoShiz

[![](https://tokei.rs/b1/github/cpp20120/CazinoShiz)](https://github.com/cpp20120/CazinoShiz)

Telegram casino mini-game bot. Russian-language UI. Games: slots (🎰), dice cube (🎲), darts (🎯), bowling (🎳), basketball (🏀), horse racing, Texas Hold'em poker, blackjack, Secret Hitler (🗳), freespin codes, leaderboard.

Built with ASP.NET Core (.NET 10), Telegram.Bot, Dapper + Npgsql (Postgres), Redis, DotNetCore.CAP, and SkiaSharp. Organized as a modular `BotFramework` host with per-game modules.

## Stack

| Layer | Tech |
|---|---|
| Runtime | ASP.NET Core, .NET 10 (preview SDK) |
| Telegram | `Telegram.Bot` 22.x (polling + webhook) |
| Persistence | PostgreSQL 16 via Dapper (`SELECT ... FOR UPDATE` on balance hot path) |
| Event bus | DotNetCore.CAP 10.x (PostgreSQL outbox + Redis transport) / InProcessEventBus fallback |
| Update fan-out | Redis Streams (opt-in, partitioned by chatId) |
| Analytics | ClickHouse 24.x buffered writer (degrades gracefully when disabled) |
| Dashboards | Grafana 11 with auto-provisioned ClickHouse datasource |
| Graphics | SkiaSharp 3.x (horse race GIF renderer) |
| Tests | xUnit, 631 tests covering framework + domain + services + router |

## Project structure

```
framework/
  BotFramework.Sdk/          module contracts (IModule, IUpdateHandler, route attrs, IEconomicsService, …)
  BotFramework.Sdk.Testing/  xUnit helpers for pure-domain module tests
  BotFramework.Host/         ASP.NET host, pipeline/router, economics, analytics, CAP, Redis Streams
games/
  Games.Dice/ Games.DiceCube/ Games.Darts/ Games.Basketball/ Games.Bowling/
  Games.Blackjack/ Games.Horse/ Games.Poker/ Games.SecretHitler/
  Games.Redeem/ Games.Leaderboard/ Games.Admin/
host/
  CasinoShiz.Host/           Program.cs — AddBotFramework().AddModule<T>()…UseBotFramework()
tests/
  CasinoShiz.Tests/          631 xUnit tests
```

## Setup

Copy `.env.example` to `.env` and fill in required fields:

```bash
cp .env.example .env
# edit .env: set Bot__Token, Bot__Username, Bot__Admins__0
```

Run locally (polling mode):

```bash
dotnet build
dotnet run --project host/CasinoShiz.Host
```

Run with full stack (Postgres + ClickHouse + Redis + Grafana):

```bash
docker compose up --build
```

Run tests:

```bash
dotnet test
```

## Admin UI

Go to `http://localhost:3000/admin/login`.

Two sign-in methods:
- **Token form** — paste the value of `Bot__AdminWebToken` from your `.env`. Works everywhere including localhost.
- **Telegram Login Widget** — requires `Bot__Username` set and the bot domain registered in BotFather (`/setdomain`). Works on public domains only.

After login, access is role-gated:
- **SuperAdmin** (`Bot__Admins`) — full access, can mutate balances and run races
- **ReadOnly** (`Bot__ReadOnlyAdmins`) — view only, mutation endpoints return 403

All write actions are logged to the `admin_audit` table.

## Configuration

Key `Bot` section fields in `appsettings.json` / env vars:

| Key | Required | Description |
|---|---|---|
| `Bot__Token` | yes | Telegram bot API token |
| `Bot__Username` | yes | Bot @username (with or without @) — used by admin login widget |
| `Bot__Admins__0` | yes | Telegram user ID with full admin access |
| `Bot__ReadOnlyAdmins__0` | no | Telegram user ID with read-only admin access |
| `Bot__AdminWebToken` | no | Token for password-based admin login (token form) |
| `Bot__IsProduction` | no | `true` → webhook mode; `false` → polling (default) |
| `Bot__TrustedChannel` | no | Channel @username for race broadcast |
| `ConnectionStrings__Postgres` | yes | Npgsql connection string |
| `Redis__Enabled` | no | `true` to enable Redis Streams + CAP Redis transport |
| `Redis__ConnectionString` | if enabled | e.g. `redis:6379` |
| `ClickHouse__Enabled` | no | `true` to enable analytics |
| `ClickHouse__Host` | if enabled | e.g. `http://clickhouse:8123` |

## License

[MIT](LICENSE)
