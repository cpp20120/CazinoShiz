# Casino

Telegram casino mini-game bot with dice games, horse racing, freespin codes, and coin management.

Built with ASP.NET Core (.NET 10), Telegram.Bot, EF Core + Dapper (Postgres), and SkiaSharp.

## Features

- **Dice Game** - Telegram slot machine with prize tables, daily attempt limits, and taxes
- **Horse Racing** - Bet on horses, watch animated GIF races rendered with SkiaSharp
- **Freespin Codes** - Admin-generated redeem codes with emoji CAPTCHA verification
- **Leaderboards** - Balance rankings and user stats
- **Analytics** - ClickHouse integration with Grafana dashboards
- **Admin Tools** - User management, payments, chat notifications

## Setup

1. Configure `src/CasinoShiz/appsettings.json`:
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
   dotnet run --project src/CasinoShiz
   ```

3. Or via Docker:
   ```bash
   docker-compose up
   ```
   Create `.env` with environment overrides. Docker setup includes ClickHouse and Grafana.

## Project Structure

```
src/
  CasinoShiz/        # Main bot application (entry point, handlers, config)
  CasinoShiz.Core/   # Core logic (services, generators, helpers)
  CasinoShiz.Data/   # Data layer (EF Core context, entities)
```

## License

[MIT](LICENSE)
