# Casino

[![](https://tokei.rs/b1/github/cpp20120/CazinoShiz)](https://github.com/cpp20120/CazinoShiz).

Telegram casino mini-game bot with dice games, horse racing, freespin codes, and coin management.

Built with ASP.NET Core (.NET 10), Telegram.Bot, Dapper + Npgsql (Postgres), and SkiaSharp. Organized as a modular `BotFramework` host with per-game modules.

## Features

- **Dice Game** - Telegram slot machine with prize tables, daily attempt limits, and taxes
- **Horse Racing** - Bet on horses, watch animated GIF races rendered with SkiaSharp
- **Poker Game** - Texas Holdem
- **BlackJack Game** - Black Jack
- **Dice Cubes** - Telegram dice cube 
- **Darts Game** - Telegram dice darts
- **Bowling Game** - Telegram dice bowling
- **Basketball Game** - Telegram dice Basketball
- **Secret Hitler** - Secret Hitler game for mutilple users
- **Freespin Codes** - Admin-generated redeem codes with emoji CAPTCHA verification
- **Leaderboards** - Balance rankings and user stats
- **Analytics** - ClickHouse integration with Grafana dashboards
- **Admin Tools** - User management, payments, chat notifications



## Setup

1. Configure `host/CasinoShiz.Host/appsettings.json`:
   ```json
   {
     "Bot": {
       "Token": "YOUR_BOT_TOKEN",
       "Admins": [123456789]
     }
   }
   ```

2. Run locally:
   ```bash
   dotnet build
   dotnet run --project host/CasinoShiz.Host
   ```

3. Or via Docker:
   ```bash
   docker-compose up
   ```
   Create `.env` with environment overrides. Docker setup includes ClickHouse and Grafana.


## Admin 

To open the admin panel go to `https://localhost:5001/admin` and login with your Telegram user ID (must be listed in `appsettings.json`).
## Project Structure

```
framework/
  BotFramework.Sdk/         # Module contracts (IModule, IUpdateHandler, route attributes, IEconomicsService, …)
  BotFramework.Sdk.Testing/ # xUnit helpers for pure-domain module tests
  BotFramework.Host/        # ASP.NET host, update pipeline/router, economics, analytics, migrations runner
games/
  Games.Dice/ Games.DiceCube/ Games.Darts/ Games.Blackjack/ Games.Horse/
  Games.Poker/ Games.SecretHitler/ Games.Redeem/
  Games.Leaderboard/ Games.Admin/
host/
  CasinoShiz.Host/          # Composition root — AddBotFramework().AddModule<T>() per shipped game
tests/
  CasinoShiz.Tests/         # xUnit tests against framework + module domain
```

Bringing up another distribution (party-games bot, trading bot, …) is the same bootstrap with a different module list — no Host edits required.

## License

[MIT](LICENSE)
