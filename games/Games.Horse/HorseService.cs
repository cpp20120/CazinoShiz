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
    Task<BetResult> PlaceBetAsync(
        long userId, string displayName, long balanceScopeId, int horseId, int amount, CancellationToken ct);

    /// <param name="balanceScopeIdOnly">If null, aggregate every chat (admin). Else this Telegram chat only.</param>
    Task<RaceInfo> GetTodayInfoAsync(long? balanceScopeIdOnly, CancellationToken ct);

    /// <summary>Local result for this chat, else today's global result (scope 0).</summary>
    Task<TodayRaceResult> GetTodayResultAsync(long viewerBalanceScopeId, CancellationToken ct);

    Task<RaceOutcome> RunRaceAsync(
        long callerUserId, HorseRunKind kind, long chatScopeId, CancellationToken ct);

    Task SaveFileIdAsync(string raceDate, long balanceScopeId, string fileId, CancellationToken ct);
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

    public async Task<BetResult> PlaceBetAsync(
        long userId, string displayName, long balanceScopeId, int horseId, int amount, CancellationToken ct)
    {
        if (horseId < 1 || horseId > _opts.HorseCount)
        {
            LogHorseBetInvalidHorse(userId, horseId);
            return BetFail(HorseError.InvalidHorseId);
        }

        await economics.EnsureUserAsync(userId, balanceScopeId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, balanceScopeId, ct);

        if (amount <= 0 || amount > balance)
        {
            LogHorseBetInvalidAmount(userId, amount, balance);
            return BetFail(HorseError.InvalidAmount, horseId, balance);
        }

        if (!await economics.TryDebitAsync(userId, balanceScopeId, amount, "horse.bet", ct))
            return BetFail(HorseError.InvalidAmount, horseId, balance);

        var raceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var bet = new HorseBetRow(Guid.NewGuid(), raceDate, userId, balanceScopeId, horseId - 1, amount);
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

    public async Task<RaceInfo> GetTodayInfoAsync(long? balanceScopeIdOnly, CancellationToken ct)
    {
        var raceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var bets = balanceScopeIdOnly is { } scope
            ? await betStore.ListByRaceDateAndScopeAsync(raceDate, scope, ct)
            : await betStore.ListByRaceDateAsync(raceDate, ct);

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < _opts.HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;

        return new RaceInfo(bets.Count, GetKoefs(stakes));
    }

    public async Task<TodayRaceResult> GetTodayResultAsync(long viewerBalanceScopeId, CancellationToken ct)
    {
        var raceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var local = await resultStore.FindAsync(raceDate, viewerBalanceScopeId, ct);
        if (local != null)
            return new TodayRaceResult(local.Winner, local.FileId);

        var global = await resultStore.FindAsync(raceDate, 0, ct);
        return global == null
            ? new TodayRaceResult(null, null)
            : new TodayRaceResult(global.Winner, global.FileId);
    }

    public Task SaveFileIdAsync(string raceDate, long balanceScopeId, string fileId, CancellationToken ct)
        => resultStore.SaveFileIdAsync(raceDate, balanceScopeId, fileId, ct);

    public async Task<RaceOutcome> RunRaceAsync(
        long callerUserId, HorseRunKind kind, long chatScopeId, CancellationToken ct)
    {
        if (!_opts.Admins.Contains(callerUserId))
        {
            LogHorseRunDenied(callerUserId);
            return RaceFail(HorseError.NotAdmin);
        }

        var raceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var resultScope = kind == HorseRunKind.Global ? 0L : chatScopeId;
        var bets = kind == HorseRunKind.Global
            ? await betStore.ListByRaceDateAsync(raceDate, ct)
            : await betStore.ListByRaceDateAndScopeAsync(raceDate, chatScopeId, ct);

        if (bets.Count < _opts.MinBetsToRun) return RaceFail(HorseError.NotEnoughBets);

        var betScopeIds = bets
            .Select(b => b.BalanceScopeId)
            .Where(scopeId => scopeId != 0)
            .Distinct()
            .ToList();

        var stakes = new Dictionary<int, int>();
        for (var i = 0; i < _opts.HorseCount; i++) stakes[i] = 0;
        foreach (var bet in bets) stakes[bet.HorseId] += bet.Amount;
        var ks = GetKoefs(stakes);

        int winner = SpeedGenerator.GenPlaces(_opts.HorseCount);
        var gifBytes = await Task.Run(() =>
        {
            var speeds = SpeedGenerator.CreateSpeeds(_opts.HorseCount, winner);
            var (frames, height, width) = HorseRaceRenderer.DrawHorses(speeds);
            return GifEncoder.RenderFramesToGif(frames, width, height);
        }, ct);

        IEnumerable<long> resultScopes = kind == HorseRunKind.Global
            ? betScopeIds.Prepend(0L).Distinct()
            : [resultScope];
        foreach (var scopeId in resultScopes)
            await resultStore.UpsertAsync(new HorseResultRow(raceDate, scopeId, winner, null), ct);

        var transactions = Payoff(bets, ks, winner);

        var payoutByKey = transactions
            .GroupBy(t => (t.UserId, t.BalanceScopeId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        foreach (var (key, prize) in payoutByKey)
            await economics.CreditAsync(key.UserId, key.BalanceScopeId, prize, "horse.payout", ct);

        var wonByUser = payoutByKey
            .GroupBy(kv => kv.Key.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        var participants = bets
            .GroupBy(b => b.UserId)
            .Select(g => new RacerSummary(
                g.Key,
                g.Sum(x => x.Amount),
                wonByUser.GetValueOrDefault(g.Key, 0)))
            .ToList();

        if (kind == HorseRunKind.Global)
            await betStore.DeleteByRaceDateAsync(raceDate, ct);
        else
            await betStore.DeleteByRaceDateAndScopeAsync(raceDate, chatScopeId, ct);

        var pot = bets.Sum(b => b.Amount);
        LogHorseRaceFinished(winner + 1, bets.Count, transactions.Count, pot);
        analytics.Track("horse", "run", new Dictionary<string, object?>
        {
            ["race_date"] = raceDate,
            ["winner"] = winner + 1,
            ["bets_count"] = bets.Count,
            ["pot"] = pot,
            ["run_kind"] = kind.ToString(),
            ["result_scope"] = resultScope,
        });
        await events.PublishAsync(new HorseRaceFinished(raceDate, winner + 1, bets.Count,
            transactions.Count, pot, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ct);

        var txForUi = transactions
            .Select(t => new RaceTransaction(t.UserId, t.BalanceScopeId, t.Amount))
            .ToList();
        return new RaceOutcome(HorseError.None, winner, gifBytes, txForUi, participants, betScopeIds);
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

    private static List<(long UserId, long BalanceScopeId, int Amount)> Payoff(
        IReadOnlyList<HorseBetRow> bets, Dictionary<int, double> ks, int winner)
    {
        return bets
            .Where(b => b.HorseId == winner)
            .Select(b => (b.UserId, b.BalanceScopeId, (int)Math.Floor(b.Amount * ks[b.HorseId])))
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
