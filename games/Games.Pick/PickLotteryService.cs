// ─────────────────────────────────────────────────────────────────────────────
// PickLotteryService — orchestrates open / join / draw / cancel for the
// /picklottery game. Talks to PickLotteryStore for persistence and to
// IEconomicsService for stake debits and payouts.
//
// Key invariant: every "stake" the user pays is debited BEFORE we attempt the
// DB write that would commit them to the pool. If the DB write loses to a
// race (or rejects), we IMMEDIATELY refund. There is never a window where
// the user has paid but isn't in a pool.
//
// Drawing (called from the sweeper):
//   1. List entries.
//   2. If count < MinEntrantsToSettle → MarkCancelledAsync, refund all stakes.
//   3. Else → pick winner uniformly, fee = floor(pot × HouseFeePercent),
//      payout = pot − fee, credit winner, MarkSettledAsync.
//
// All drawing/cancellation paths return a typed result so the caller (the
// sweeper job) can post the right message to the chat.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using Microsoft.Extensions.Options;

namespace Games.Pick;

public enum LotteryOpenStatus
{
    Ok,
    InvalidStake,
    NotEnoughCoins,
    AlreadyOpen,
    Failed,
}

public enum LotteryJoinStatus
{
    Ok,
    NoOpenLottery,
    AlreadyJoined,
    NotEnoughCoins,
    Failed,
}

public enum LotterySettleKind
{
    Settled,        // winner picked, payout credited
    Cancelled,      // below quorum, all stakes refunded
}

public sealed record LotteryOpenResult(
    LotteryOpenStatus Status,
    PickLotteryRow? Row,
    int Balance);

public sealed record LotteryJoinResult(
    LotteryJoinStatus Status,
    PickLotteryRow? Row,
    int Entrants,
    int PotSoFar,
    int Balance);

public sealed record LotteryInfoSnapshot(
    PickLotteryRow Row,
    int Entrants,
    int PotSoFar);

public sealed record LotterySettleResult(
    LotterySettleKind Kind,
    PickLotteryRow Row,
    IReadOnlyList<PickLotteryEntryRow> Entries,
    long? WinnerId,
    string? WinnerName,
    int Pot,
    int Fee,
    int Payout);

public interface IPickLotteryService
{
    Task<LotteryOpenResult> OpenAsync(
        long userId, string displayName, long chatId, int stake, CancellationToken ct);

    Task<LotteryJoinResult> JoinAsync(
        long userId, string displayName, long chatId, CancellationToken ct);

    /// <summary>Returns the open pool and a fresh entry count, or null if no pool is open in this chat.</summary>
    Task<LotteryInfoSnapshot?> InfoAsync(long chatId, CancellationToken ct);

    /// <summary>Cancel + refund. Caller must be the opener. No-op if pool is no longer open.</summary>
    Task<LotterySettleResult?> CancelByOpenerAsync(long openerId, long chatId, CancellationToken ct);

    /// <summary>Sweeper-driven draw or cancel for one expired pool.</summary>
    Task<LotterySettleResult> SettleAsync(PickLotteryRow row, CancellationToken ct);
}

public sealed partial class PickLotteryService(
    IPickLotteryStore store,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IOptions<PickOptions> options,
    ILogger<PickLotteryService> logger) : IPickLotteryService
{
    private PickLotteryOptions Opts => options.Value.Lottery;

    public async Task<LotteryOpenResult> OpenAsync(
        long userId, string displayName, long chatId, int stake, CancellationToken ct)
    {
        var o = Opts;
        if (stake < Math.Max(1, o.MinStake) || (o.MaxStake > 0 && stake > o.MaxStake))
            return new LotteryOpenResult(LotteryOpenStatus.InvalidStake, null, 0);

        // Cheap pre-check; the unique-index race is still handled below.
        var existing = await store.FindOpenByChatAsync(chatId, ct);
        if (existing is not null)
            return new LotteryOpenResult(LotteryOpenStatus.AlreadyOpen, existing, 0);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (stake > balance)
            return new LotteryOpenResult(LotteryOpenStatus.NotEnoughCoins, null, balance);

        if (!await economics.TryDebitAsync(userId, chatId, stake, "pick.lottery.open", ct))
        {
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return new LotteryOpenResult(LotteryOpenStatus.NotEnoughCoins, null, balance);
        }

        var id = Guid.NewGuid();
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(10, o.DurationSeconds));
        var (err, row) = await store.InsertOpenAsync(id, chatId, userId, displayName, stake, deadline, ct);

        if (err == LotteryOpenError.AlreadyOpenInChat || row is null)
        {
            // Lost the race or hit the partial unique constraint — refund.
            await economics.CreditAsync(userId, chatId, stake, "pick.lottery.open.refund", ct);
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            var fresh = await store.FindOpenByChatAsync(chatId, ct);
            return new LotteryOpenResult(LotteryOpenStatus.AlreadyOpen, fresh, balance);
        }

        balance = await economics.GetBalanceAsync(userId, chatId, ct);
        analytics.Track("pick", "lottery.open", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["stake"] = stake,
            ["lottery_id"] = id.ToString(),
        });
        LogLotteryOpened(id, chatId, userId, stake);
        return new LotteryOpenResult(LotteryOpenStatus.Ok, row, balance);
    }

    public async Task<LotteryJoinResult> JoinAsync(
        long userId, string displayName, long chatId, CancellationToken ct)
    {
        var lottery = await store.FindOpenByChatAsync(chatId, ct);
        if (lottery is null)
            return new LotteryJoinResult(LotteryJoinStatus.NoOpenLottery, null, 0, 0, 0);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (lottery.Stake > balance)
            return new LotteryJoinResult(LotteryJoinStatus.NotEnoughCoins, lottery, 0, 0, balance);

        if (!await economics.TryDebitAsync(userId, chatId, lottery.Stake, "pick.lottery.join", ct))
        {
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return new LotteryJoinResult(LotteryJoinStatus.NotEnoughCoins, lottery, 0, 0, balance);
        }

        var (err, row) = await store.AddEntryAsync(chatId, userId, displayName, ct);
        if (err == LotteryJoinError.AlreadyJoined)
        {
            await economics.CreditAsync(userId, chatId, lottery.Stake, "pick.lottery.join.refund", ct);
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return new LotteryJoinResult(LotteryJoinStatus.AlreadyJoined, row ?? lottery, 0, 0, balance);
        }
        if (err == LotteryJoinError.NoOpenLottery || row is null)
        {
            await economics.CreditAsync(userId, chatId, lottery.Stake, "pick.lottery.join.refund", ct);
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return new LotteryJoinResult(LotteryJoinStatus.NoOpenLottery, null, 0, 0, balance);
        }

        balance = await economics.GetBalanceAsync(userId, chatId, ct);
        var entries = await store.ListEntriesAsync(row.Id, ct);
        var pot = entries.Sum(e => e.StakePaid);

        analytics.Track("pick", "lottery.join", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["stake"] = row.Stake,
            ["lottery_id"] = row.Id.ToString(), ["entrants"] = entries.Count,
        });
        return new LotteryJoinResult(LotteryJoinStatus.Ok, row, entries.Count, pot, balance);
    }

    public async Task<LotteryInfoSnapshot?> InfoAsync(long chatId, CancellationToken ct)
    {
        var row = await store.FindOpenByChatAsync(chatId, ct);
        if (row is null) return null;
        var entries = await store.ListEntriesAsync(row.Id, ct);
        var pot = entries.Sum(e => e.StakePaid);
        return new LotteryInfoSnapshot(row, entries.Count, pot);
    }

    public async Task<LotterySettleResult?> CancelByOpenerAsync(long openerId, long chatId, CancellationToken ct)
    {
        var lottery = await store.FindOpenByChatAsync(chatId, ct);
        if (lottery is null || lottery.OpenerId != openerId)
            return null;

        return await SettleInternalAsync(lottery, forceCancel: true, ct);
    }

    public Task<LotterySettleResult> SettleAsync(PickLotteryRow row, CancellationToken ct) =>
        SettleInternalAsync(row, forceCancel: false, ct);

    private async Task<LotterySettleResult> SettleInternalAsync(
        PickLotteryRow row, bool forceCancel, CancellationToken ct)
    {
        var entries = await store.ListEntriesAsync(row.Id, ct);
        var pot = entries.Sum(e => e.StakePaid);
        var minEntrants = Math.Max(2, Opts.MinEntrantsToSettle);

        var shouldCancel = forceCancel || entries.Count < minEntrants;
        if (shouldCancel)
        {
            // Refund every entry, then transition to cancelled. We refund
            // FIRST: if the cancel UPDATE loses a race (someone already
            // settled the row), we'd rather double-credit than under-credit.
            foreach (var e in entries)
            {
                await economics.CreditAsync(
                    e.UserId, row.ChatId, e.StakePaid,
                    forceCancel ? "pick.lottery.cancel.refund" : "pick.lottery.refund", ct);
            }

            var moved = await store.MarkCancelledAsync(row.Id, ct);
            if (moved)
            {
                analytics.Track("pick", "lottery.cancelled", new Dictionary<string, object?>
                {
                    ["chat_id"] = row.ChatId,
                    ["lottery_id"] = row.Id.ToString(),
                    ["entrants"] = entries.Count,
                    ["pot"] = pot,
                    ["forced"] = forceCancel,
                });
                LogLotteryCancelled(row.Id, row.ChatId, entries.Count, forceCancel);
            }
            return new LotterySettleResult(LotterySettleKind.Cancelled, row, entries, null, null, pot, 0, 0);
        }

        // Draw
        var winnerIdx = Random.Shared.Next(entries.Count);
        var winner = entries[winnerIdx];
        var fee = (int)Math.Max(0, Math.Floor(pot * Math.Clamp(Opts.HouseFeePercent, 0.0, 1.0)));
        var payout = Math.Max(0, pot - fee);

        if (payout > 0)
            await economics.CreditAsync(winner.UserId, row.ChatId, payout, "pick.lottery.win", ct);

        var marked = await store.MarkSettledAsync(
            row.Id, winner.UserId, winner.DisplayName, pot, payout, fee, ct);
        if (!marked)
        {
            // Something else (manual cancel?) won — undo our credit.
            if (payout > 0)
                await economics.DebitAsync(winner.UserId, row.ChatId, payout, "pick.lottery.win.rollback", ct);
            return new LotterySettleResult(LotterySettleKind.Cancelled, row, entries, null, null, pot, 0, 0);
        }

        analytics.Track("pick", "lottery.settled", new Dictionary<string, object?>
        {
            ["chat_id"] = row.ChatId,
            ["lottery_id"] = row.Id.ToString(),
            ["entrants"] = entries.Count,
            ["pot"] = pot,
            ["fee"] = fee,
            ["payout"] = payout,
            ["winner_id"] = winner.UserId,
        });
        LogLotterySettled(row.Id, row.ChatId, entries.Count, pot, payout, winner.UserId);

        return new LotterySettleResult(
            LotterySettleKind.Settled, row, entries,
            winner.UserId, winner.DisplayName, pot, fee, payout);
    }

    [LoggerMessage(EventId = 5921, Level = LogLevel.Information,
        Message = "pick.lottery.opened id={Id} chat={ChatId} opener={OpenerId} stake={Stake}")]
    partial void LogLotteryOpened(Guid id, long chatId, long openerId, int stake);

    [LoggerMessage(EventId = 5922, Level = LogLevel.Information,
        Message = "pick.lottery.cancelled id={Id} chat={ChatId} entrants={Entrants} forced={Forced}")]
    partial void LogLotteryCancelled(Guid id, long chatId, int entrants, bool forced);

    [LoggerMessage(EventId = 5923, Level = LogLevel.Information,
        Message = "pick.lottery.settled id={Id} chat={ChatId} entrants={Entrants} pot={Pot} payout={Payout} winner={WinnerId}")]
    partial void LogLotterySettled(Guid id, long chatId, int entrants, int pot, int payout, long winnerId);
}
