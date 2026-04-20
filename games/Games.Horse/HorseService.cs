// ─────────────────────────────────────────────────────────────────────────────
// HorseService — daily horse-race betting pool. Ported from
// src/CasinoShiz.Core/Services/Horse/HorseService.cs:
//
//   • EF Core HorseBet/HorseResult rows → IHorseBetStore / IHorseResultStore.
//   • EconomicsService.Debit/Credit now take userId, not an entity.
//   • BotOptions.Admins gate replaced by per-module HorseOptions.Admins —
//     modules own their own access policy.
//
// Pool math is identical: each horse's koef = (pot - stake_on_horse) /
// (1.1 * stake_on_horse) + 1, floored to 3 decimals. The winning bettor's
// payout = bet * koef (integer-floored).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Games.Horse.Generators;
using Microsoft.Extensions.Options;
using static Games.Horse.HorseResultHelpers;

namespace Games.Horse;

public interface IHorseService
{
    Task<BetResult> PlaceBetAsync(long userId, string displayName, int horseId, int amount, CancellationToken ct);
    Task<RaceInfo> GetTodayInfoAsync(CancellationToken ct);
    Task<TodayRaceResult> GetTodayResultAsync(CancellationToken ct);
    Task<RaceOutcome> RunRaceAsync(long callerUserId, CancellationToken ct);
}

public sealed partial class HorseService(
    IHorseBetStore betStore,
    IHorseResultStore resultStore,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDomainEventBus events,
    IOptions<HorseOptions> options,
    ILogger<HorseService> logger) : IHorseService
{
    private readonly HorseOptions _opts = options.Value;

    public int HorseCount => _opts.HorseCount;
    public int MinBetsToRun => _opts.MinBetsToRun;

    public async Task<BetResult> PlaceBetAsync(long userId, string displayName, int horseId, int amount, CancellationToken ct)
    {
        if (horseId < 1 || horseId > _opts.HorseCount)
        {
            LogHorseBetInvalidHorse(userId, horseId);
            return BetFail(HorseError.InvalidHorseId);
        }

        await economics.EnsureUserAsync(userId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);

        if (amount <= 0 || amount > balance)
        {
            LogHorseBetInvalidAmount(userId, amount, balance);
            return BetFail(HorseError.InvalidAmount, horseId, balance);
        }

        if (!await economics.TryDebitAsync(userId, amount, "horse.bet", ct))
            return BetFail(HorseError.InvalidAmount, horseId, balance);

        var raceDate = HorseTimeHelper.GetRaceDate();
        var bet = new HorseBetRow(Guid.NewGuid(), raceDate, userId, horseId - 1, amount);
        await betStore.InsertAsync(bet, ct);

        LogHorseBetPlaced(userId, horseId, amount, raceDate);
        analytics.Track("horse", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["horse_id"] = horseId, ["amount"] = amount, ["race_date"] = raceDate,
        });
        await events.PublishAsync(new HorseBetPlaced(userId, horseId, amount, raceDate,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);

        return new BetResult(HorseError.None, horseId, amount, balance - amount);
    }

    public async Task<RaceInfo> GetTodayInfoAsync(CancellationToken ct)
    {
        var raceDate = HorseTimeHelper.GetRaceDate();
        var bets = await betStore.ListByRaceDateAsync(raceDate, ct);

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < _opts.HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;

        return new RaceInfo(bets.Count, GetKoefs(stakes));
    }

    public async Task<TodayRaceResult> GetTodayResultAsync(CancellationToken ct)
    {
        var raceDate = HorseTimeHelper.GetRaceDate();
        var result = await resultStore.FindAsync(raceDate, ct);
        return result == null
            ? new TodayRaceResult(null, null)
            : new TodayRaceResult(result.Winner, result.ImageData);
    }

    public async Task<RaceOutcome> RunRaceAsync(long callerUserId, CancellationToken ct)
    {
        if (!_opts.Admins.Contains(callerUserId))
        {
            LogHorseRunDenied(callerUserId);
            return RaceFail(HorseError.NotAdmin);
        }

        var raceDate = HorseTimeHelper.GetRaceDate();
        var bets = await betStore.ListByRaceDateAsync(raceDate, ct);

        if (bets.Count < _opts.MinBetsToRun) return RaceFail(HorseError.NotEnoughBets);

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < _opts.HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;
        var ks = GetKoefs(stakes);

        int winner = SpeedGenerator.GenPlaces(_opts.HorseCount);
        var (gifBytes, lastFrame) = await Task.Run(() =>
        {
            var speeds = SpeedGenerator.CreateSpeeds(_opts.HorseCount, winner);
            var (frames, height, width) = HorseRaceRenderer.DrawHorses(speeds);
            var gif = GifEncoder.RenderFramesToGif(frames, width, height);
            return (gif, frames[^1]);
        }, ct);

        await resultStore.UpsertAsync(new HorseResultRow(raceDate, winner, lastFrame), ct);

        var transactions = Payoff(bets, ks, winner);

        var payoutByUser = transactions
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        foreach (var (uid, prize) in payoutByUser)
            await economics.CreditAsync(uid, prize, "horse.payout", ct);

        var participants = bets
            .GroupBy(b => b.UserId)
            .Select(g => new RacerSummary(
                g.Key,
                g.Sum(x => x.Amount),
                payoutByUser.GetValueOrDefault(g.Key, 0)))
            .ToList();

        await betStore.DeleteByRaceDateAsync(raceDate, ct);

        var pot = bets.Sum(b => b.Amount);
        LogHorseRaceFinished(winner + 1, bets.Count, transactions.Count, pot);
        analytics.Track("horse", "run", new Dictionary<string, object?>
        {
            ["race_date"] = raceDate, ["winner"] = winner + 1,
            ["bets_count"] = bets.Count, ["pot"] = pot,
        });
        await events.PublishAsync(new HorseRaceFinished(raceDate, winner + 1, bets.Count,
            transactions.Count, pot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);

        return new RaceOutcome(HorseError.None, winner, gifBytes, transactions, participants);
    }

    public static Dictionary<int, double> GetKoefs(Dictionary<int, int> stakes)
    {
        var sum = stakes.Values.Sum();
        return stakes.ToDictionary(
            kv => kv.Key,
            kv => kv.Value == 0
                ? 1.0
                : Math.Floor((sum - kv.Value) / (1.1 * kv.Value) * 1000) / 1000 + 1
        );
    }

    private static List<(long UserId, int Amount)> Payoff(
        IReadOnlyList<HorseBetRow> bets, Dictionary<int, double> ks, int winner)
    {
        return bets
            .Where(b => b.HorseId == winner)
            .Select(b => (b.UserId, (int)Math.Floor(b.Amount * ks[b.HorseId])))
            .ToList();
    }

    [LoggerMessage(LogLevel.Information, "horse.bet.rejected user={UserId} reason=invalid_horse horse={Horse}")]
    partial void LogHorseBetInvalidHorse(long userId, int horse);

    [LoggerMessage(LogLevel.Information, "horse.bet.rejected user={UserId} reason=invalid_amount amount={Amount} balance={Coins}")]
    partial void LogHorseBetInvalidAmount(long userId, int amount, int coins);

    [LoggerMessage(LogLevel.Information, "horse.bet.ok user={UserId} horse={Horse} amount={Amount} race_date={Date}")]
    partial void LogHorseBetPlaced(long userId, int horse, int amount, string date);

    [LoggerMessage(LogLevel.Warning, "horse.run.rejected user={UserId} reason=not_admin")]
    partial void LogHorseRunDenied(long userId);

    [LoggerMessage(LogLevel.Information, "horse.run.ok winner={Winner} bets={Bets} payouts={Payouts} pot={Pot}")]
    partial void LogHorseRaceFinished(int winner, int bets, int payouts, int pot);
}
