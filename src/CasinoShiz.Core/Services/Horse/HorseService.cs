using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Generators;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CasinoShiz.Services.Horse.HorseResultHelpers;

namespace CasinoShiz.Services.Horse;

public sealed partial class HorseService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    ILogger<HorseService> logger)
{
    public const int HorseCount = 4;
    private readonly BotOptions _opts = options.Value;

    public async Task<BetResult> PlaceBetAsync(long userId, string displayName, int horseId, int amount, CancellationToken ct)
    {
        if (horseId < 1 || horseId > HorseCount)
        {
            LogHorseBetRejectedUserUseridReasonInvalidHorseHorseHorse(userId, horseId);
            return BetFail(HorseError.InvalidHorseId);
        }

        var user = await EnsureUserAsync(userId, displayName, ct);

        if (amount <= 0 || amount > user.Coins)
        {
            LogHorseBetRejectedUserUseridReasonInvalidAmountAmountAmountBalanceCoins(userId, amount, user.Coins);
            return BetFail(HorseError.InvalidAmount, horseId, user.Coins);
        }

        var bet = new HorseBet
        {
            Id = Guid.NewGuid(),
            RaceDate = TimeHelper.GetRaceDate(),
            HorseId = horseId - 1,
            Amount = amount,
            UserId = userId,
        };

        user.Coins -= amount;
        db.HorseBets.Add(bet);
        await db.SaveChangesAsync(ct);

        LogHorseBetOkUserUseridHorseHorseAmountAmountRaceDateDate(userId, horseId, amount, bet.RaceDate);
        reporter.SendEvent(new EventData
        {
            EventType = "horse_bet",
            Payload = new { user_id = userId, horse_id = horseId, amount, race_date = bet.RaceDate }
        });

        return new BetResult(HorseError.None, horseId, amount, user.Coins);
    }

    public async Task<RaceInfo> GetTodayInfoAsync(CancellationToken ct)
    {
        var raceDate = TimeHelper.GetRaceDate();
        var bets = await db.HorseBets.Where(b => b.RaceDate == raceDate).ToListAsync(ct);

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;

        return new RaceInfo(bets.Count, GetKoefs(stakes));
    }

    public async Task<TodayResult> GetTodayResultAsync(CancellationToken ct)
    {
        var raceDate = TimeHelper.GetRaceDate();
        var result = await db.HorseResults.FindAsync([raceDate], ct);
        return new TodayResult(result);
    }

    public async Task<RaceOutcome> RunRaceAsync(long callerUserId, CancellationToken ct)
    {
        if (!_opts.Admins.Contains(callerUserId))
        {
            LogHorseRunRejectedUserUseridReasonNotAdmin(callerUserId);
            return RaceFail(HorseError.NotAdmin);
        }

        var raceDate = TimeHelper.GetRaceDate();
        var bets = await db.HorseBets.Where(b => b.RaceDate == raceDate).ToListAsync(ct);

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;
        var ks = GetKoefs(stakes);

        int winner = SpeedGenerator.GenPlaces(HorseCount);
        var speeds = SpeedGenerator.CreateSpeeds(HorseCount, winner);
        var (frames, height, width) = HorseRaceRenderer.DrawHorses(speeds);
        var gifBytes = GifEncoder.RenderFramesToGif(frames, width, height);
        var lastFrame = frames[^1];

        var existing = await db.HorseResults.FindAsync([raceDate], ct);
        if (existing != null)
        {
            existing.Winner = winner;
            existing.ImageData = lastFrame;
        }
        else
        {
            db.HorseResults.Add(new HorseResult
            {
                RaceDate = raceDate,
                Winner = winner,
                ImageData = lastFrame,
            });
        }
        await db.SaveChangesAsync(ct);

        var transactions = Payoff(bets, ks, winner);

        foreach (var (uid, prize) in transactions.GroupBy(t => t.UserId).Select(g => (g.Key, g.Sum(x => x.Amount))))
        {
            var user = await db.Users.FindAsync([uid], ct);
            if (user != null) user.Coins += prize;
        }
        db.HorseBets.RemoveRange(bets);
        await db.SaveChangesAsync(ct);

        LogHorseRunOkWinnerWinnerBetsBetsPayoutsPayoutsPotPot(winner + 1, bets.Count, transactions.Count, bets.Sum(b => b.Amount));
        reporter.SendEvent(new EventData
        {
            EventType = "horse_run",
            Payload = new
            {
                race_date = raceDate,
                winner = winner + 1,
                bets_count = bets.Count,
                winners = transactions.Select(t => new { user_id = t.UserId, amount = t.Amount })
            }
        });

        return new RaceOutcome(HorseError.None, winner, gifBytes, transactions);
    }

    public static Dictionary<int, double> GetKoefs(Dictionary<int, int> stakes)
    {
        var sum = stakes.Values.Sum();
        return stakes.ToDictionary(
            kv => kv.Key,
            kv => Math.Floor((sum - kv.Value) / (1.1 * kv.Value) * 1000) / 1000 + 1
        );
    }

    private static List<(long UserId, int Amount)> Payoff(List<HorseBet> bets, Dictionary<int, double> ks, int winner)
    {
        return bets
            .Where(b => b.HorseId == winner)
            .Select(b => (b.UserId, (int)Math.Floor(b.Amount * ks[b.HorseId])))
            .ToList();
    }

    private async Task<UserState> EnsureUserAsync(long userId, string displayName, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user == null)
        {
            user = new UserState
            {
                TelegramUserId = userId,
                DisplayName = displayName,
                Coins = 100,
                LastDayUtc = TimeHelper.GetCurrentDayMillis(),
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    [LoggerMessage(LogLevel.Information, "horse.bet.rejected user={UserId} reason=invalid_horse horse={Horse}")]
    partial void LogHorseBetRejectedUserUseridReasonInvalidHorseHorseHorse(long userId, int horse);

    [LoggerMessage(LogLevel.Information, "horse.bet.rejected user={UserId} reason=invalid_amount amount={Amount} balance={Coins}")]
    partial void LogHorseBetRejectedUserUseridReasonInvalidAmountAmountAmountBalanceCoins(long userId, int amount, int coins);

    [LoggerMessage(LogLevel.Information, "horse.bet.ok user={UserId} horse={Horse} amount={Amount} race_date={Date}")]
    partial void LogHorseBetOkUserUseridHorseHorseAmountAmountRaceDateDate(long userId, int horse, int amount, string date);

    [LoggerMessage(LogLevel.Warning, "horse.run.rejected user={UserId} reason=not_admin")]
    partial void LogHorseRunRejectedUserUseridReasonNotAdmin(long userId);

    [LoggerMessage(LogLevel.Information, "horse.run.ok winner={Winner} bets={Bets} payouts={Payouts} pot={Pot}")]
    partial void LogHorseRunOkWinnerWinnerBetsBetsPayoutsPayoutsPotPot(int winner, int bets, int payouts, int pot);
}
