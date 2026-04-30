// ─────────────────────────────────────────────────────────────────────────────
// PickDailyLotteryService — orchestrates the per-chat daily ticket pool.
//
// Day boundary:
//   The day is keyed by local-date in UTC + (hoursEastOfUtc) — the same
//   offset the dice-daily-limit system uses (configured under
//   Bot:TelegramDiceDailyLimit:TimezoneOffsetHours, default 7). PickDaily
//   options can override via TimezoneOffsetHoursOverride.
//
// Buy flow:
//   1. Compute today's local date and corresponding UTC deadline (midnight
//      of NEXT local day expressed in UTC).
//   2. GetOrCreate the lottery row for (chat, today). Idempotent + race-safe.
//   3. If the row's deadline has already passed (clock skew at midnight ±
//      a few seconds), bow out — the sweeper will draw it shortly. The user
//      can retry the next day.
//   4. EnsureUser, check balance ≥ count × ticket_price, debit total in one
//      shot, then INSERT N ticket rows. If the insert fails partway, refund.
//
// Settle flow (sweeper-driven):
//   • 0 tickets → MarkCancelled. No coins moved (nothing was paid in).
//   • ≥1 tickets → PickRandomWinner (uniform over all ticket rows), credit
//     pot − fee to winner, MarkSettled.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using Microsoft.Extensions.Options;

namespace Games.Pick;

public enum DailyBuyStatus
{
    Ok,
    InvalidCount,
    OverDailyCap,
    OverPerCommandCap,
    NotEnoughCoins,
    DayAlreadyClosing,
    Failed,
}

public sealed record DailyBuyResult(
    DailyBuyStatus Status,
    PickDailyLotteryRow? Row,
    int TicketsBought,
    int TotalUserTickets,
    int TotalTickets,
    int PotTotal,
    int Balance);

public sealed record DailyInfoSnapshot(
    PickDailyLotteryRow Row,
    int TicketsTotal,
    int DistinctUsers,
    int PotTotal,
    int ViewerTickets,
    IReadOnlyList<PickDailyTicketSummary> TopHolders);

public sealed record DailySettleResult(
    bool Drawn,
    PickDailyLotteryRow Row,
    int TicketsTotal,
    int DistinctUsers,
    int PotTotal,
    int Fee,
    int Payout,
    long? WinnerId,
    string? WinnerName,
    int? WinnerTicketCount);

public interface IPickDailyLotteryService
{
    Task<DailyBuyResult> BuyAsync(
        long userId, string displayName, long chatId, int count, CancellationToken ct);

    Task<DailyInfoSnapshot?> InfoAsync(long chatId, long viewerId, CancellationToken ct);

    /// <summary>Sweeper-driven draw or cancel for one expired pool.</summary>
    Task<DailySettleResult> SettleAsync(PickDailyLotteryRow row, CancellationToken ct);

    Task<IReadOnlyList<PickDailyLotteryRow>> HistoryAsync(long chatId, int limit, CancellationToken ct);

    /// <summary>Calendar date (in the configured offset) of the next-upcoming draw.</summary>
    DateOnly LocalToday();

    /// <summary>UTC instant of the next-upcoming local draw (cycle end).</summary>
    DateTime LocalNextDrawUtc();

    int OffsetHours { get; }

    /// <summary>Local hour-of-day at which the daily cycle closes (0..23).</summary>
    int DrawHourLocal { get; }
}

public sealed partial class PickDailyLotteryService(
    IPickDailyLotteryStore store,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IOptions<PickOptions> pickOptions,
    IOptions<TelegramDiceDailyLimitOptions> diceLimitOptions,
    ILogger<PickDailyLotteryService> logger) : IPickDailyLotteryService
{
    private PickDailyLotteryOptions Opts => pickOptions.Value.Daily;

    public int OffsetHours
    {
        get
        {
            var override_ = Opts.TimezoneOffsetHoursOverride;
            return override_ != 0 ? override_ : diceLimitOptions.Value.TimezoneOffsetHours;
        }
    }

    public int DrawHourLocal => Math.Clamp(Opts.DrawHourLocal, 0, 23);

    /// <summary>
    /// Calendar date (local) of the next-upcoming draw.
    /// • If <c>now &lt;= today's draw time</c> → today's date
    /// • Else (we're past today's draw time)   → tomorrow's date
    /// This means buying immediately after the draw lands you in the
    /// NEXT cycle, with no dead window where /dailylottery is closed.
    /// </summary>
    public DateOnly LocalToday()
    {
        var offset = TimeSpan.FromHours(OffsetHours);
        var nowLocal = DateTimeOffset.UtcNow.ToOffset(offset);
        var todayDrawAt = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day,
            DrawHourLocal, 0, 0, offset);
        return nowLocal <= todayDrawAt
            ? DateOnly.FromDateTime(nowLocal.Date)
            : DateOnly.FromDateTime(nowLocal.Date.AddDays(1));
    }

    public DateTime LocalNextDrawUtc()
    {
        var offset = TimeSpan.FromHours(OffsetHours);
        var drawDate = LocalToday();
        var drawAtLocal = new DateTimeOffset(
            drawDate.Year, drawDate.Month, drawDate.Day,
            DrawHourLocal, 0, 0, offset);
        return drawAtLocal.UtcDateTime;
    }

    // ── buy ──────────────────────────────────────────────────────────────────

    public async Task<DailyBuyResult> BuyAsync(
        long userId, string displayName, long chatId, int count, CancellationToken ct)
    {
        var perCommandCap = Math.Max(1, Opts.MaxTicketsPerBuyCommand);
        if (count <= 0)
            return new DailyBuyResult(DailyBuyStatus.InvalidCount, null, 0, 0, 0, 0, 0);

        if (count > perCommandCap)
            return new DailyBuyResult(DailyBuyStatus.OverPerCommandCap, null, 0, 0, 0, 0, 0);

        var ticketPrice = Math.Max(1, Opts.TicketPrice);
        var dayLocal = LocalToday();
        var deadlineUtc = LocalNextDrawUtc();

        // Edge case: if we're inside the window where the previous day's
        // deadline has passed but the sweeper hasn't yet rolled it over, we
        // could still match the open row from "yesterday" with deadline in
        // the past. Handled via the freshness check below.
        var lottery = await store.GetOrCreateOpenAsync(chatId, dayLocal, ticketPrice, deadlineUtc, ct);
        if (lottery.Status != "open" || lottery.DeadlineAt <= DateTime.UtcNow)
            return new DailyBuyResult(DailyBuyStatus.DayAlreadyClosing, lottery, 0, 0, 0, 0, 0);

        // Per-user daily cap.
        var alreadyOwned = await store.CountUserTicketsAsync(lottery.Id, userId, ct);
        var userCap = Opts.MaxTicketsPerUserPerDay;
        if (userCap > 0 && alreadyOwned + count > userCap)
            return new DailyBuyResult(DailyBuyStatus.OverDailyCap, lottery, 0, alreadyOwned, 0, 0, 0);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        var totalCost = count * lottery.TicketPrice;
        if (totalCost > balance)
            return new DailyBuyResult(DailyBuyStatus.NotEnoughCoins, lottery, 0, alreadyOwned, 0, 0, balance);

        if (!await economics.TryDebitAsync(userId, chatId, totalCost, "pick.daily.buy", ct))
        {
            balance = await economics.GetBalanceAsync(userId, chatId, ct);
            return new DailyBuyResult(DailyBuyStatus.NotEnoughCoins, lottery, 0, alreadyOwned, 0, 0, balance);
        }

        try
        {
            var inserted = await store.InsertTicketsAsync(
                lottery.Id, userId, displayName, count, lottery.TicketPrice, ct);
            if (inserted != count)
            {
                // Best-effort refund the gap; insert never partially fails
                // in our schema, but cover the pathological case anyway.
                var refundAmount = (count - inserted) * lottery.TicketPrice;
                if (refundAmount > 0)
                    await economics.CreditAsync(userId, chatId, refundAmount, "pick.daily.buy.refund", ct);
            }
        }
        catch
        {
            await economics.CreditAsync(userId, chatId, totalCost, "pick.daily.buy.refund", ct);
            throw;
        }

        var totalUser = alreadyOwned + count;
        var totalTickets = await store.CountTicketsAsync(lottery.Id, ct);
        var pot = totalTickets * lottery.TicketPrice;
        balance = await economics.GetBalanceAsync(userId, chatId, ct);

        analytics.Track("pick", "daily.buy", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["lottery_id"] = lottery.Id.ToString(),
            ["count"] = count,
            ["ticket_price"] = lottery.TicketPrice,
            ["total_user_tickets"] = totalUser,
            ["total_tickets"] = totalTickets,
        });
        LogBuy(userId, chatId, lottery.Id, count, totalUser);

        return new DailyBuyResult(DailyBuyStatus.Ok, lottery, count, totalUser, totalTickets, pot, balance);
    }

    // ── info ─────────────────────────────────────────────────────────────────

    public async Task<DailyInfoSnapshot?> InfoAsync(long chatId, long viewerId, CancellationToken ct)
    {
        var dayLocal = LocalToday();
        var lottery = await store.FindOpenByChatAsync(chatId, dayLocal, ct);
        if (lottery is null) return null;

        var summaries = await store.ListUserTicketCountsAsync(lottery.Id, ct);
        var totalTickets = summaries.Sum(s => s.TicketCount);
        var pot = totalTickets * lottery.TicketPrice;
        var viewerTickets = summaries
            .Where(s => s.UserId == viewerId)
            .Select(s => s.TicketCount)
            .FirstOrDefault();

        // Top holders for display — cap at 10 to avoid spammy messages.
        var top = summaries.Take(10).ToList();
        return new DailyInfoSnapshot(lottery, totalTickets, summaries.Count, pot, viewerTickets, top);
    }

    // ── settle ───────────────────────────────────────────────────────────────

    public async Task<DailySettleResult> SettleAsync(PickDailyLotteryRow row, CancellationToken ct)
    {
        var totalTickets = await store.CountTicketsAsync(row.Id, ct);
        var distinctUsers = await store.CountDistinctUsersAsync(row.Id, ct);
        var pot = totalTickets * row.TicketPrice;

        if (totalTickets == 0)
        {
            await store.MarkCancelledAsync(row.Id, ct);
            analytics.Track("pick", "daily.cancelled_empty", new Dictionary<string, object?>
            {
                ["chat_id"] = row.ChatId,
                ["lottery_id"] = row.Id.ToString(),
                ["day_local"] = row.DayLocal.ToString("yyyy-MM-dd"),
            });
            LogSettleCancelledEmpty(row.Id, row.ChatId);
            return new DailySettleResult(
                Drawn: false, Row: row, TicketsTotal: 0, DistinctUsers: 0, PotTotal: 0,
                Fee: 0, Payout: 0, WinnerId: null, WinnerName: null, WinnerTicketCount: null);
        }

        var winner = await store.PickRandomWinnerAsync(row.Id, ct);
        if (winner is null)
        {
            // Concurrent sweeper or someone deleted tickets — treat as empty.
            await store.MarkCancelledAsync(row.Id, ct);
            return new DailySettleResult(
                Drawn: false, Row: row, TicketsTotal: 0, DistinctUsers: 0, PotTotal: 0,
                Fee: 0, Payout: 0, WinnerId: null, WinnerName: null, WinnerTicketCount: null);
        }

        var fee = (int)Math.Max(0, Math.Floor(pot * Math.Clamp(Opts.HouseFeePercent, 0.0, 1.0)));
        var payout = Math.Max(0, pot - fee);

        if (payout > 0)
            await economics.CreditAsync(winner.Value.UserId, row.ChatId, payout, "pick.daily.win", ct);

        var marked = await store.MarkSettledAsync(
            row.Id, winner.Value.UserId, winner.Value.DisplayName,
            totalTickets, pot, payout, fee, ct);

        if (!marked)
        {
            // Lost the race to another sweeper instance — undo our credit.
            if (payout > 0)
                await economics.DebitAsync(winner.Value.UserId, row.ChatId, payout, "pick.daily.win.rollback", ct);
            return new DailySettleResult(
                Drawn: false, Row: row, TicketsTotal: totalTickets, DistinctUsers: distinctUsers,
                PotTotal: pot, Fee: 0, Payout: 0,
                WinnerId: null, WinnerName: null, WinnerTicketCount: null);
        }

        var winnerTickets = await store.CountUserTicketsAsync(row.Id, winner.Value.UserId, ct);
        analytics.Track("pick", "daily.settled", new Dictionary<string, object?>
        {
            ["chat_id"] = row.ChatId,
            ["lottery_id"] = row.Id.ToString(),
            ["day_local"] = row.DayLocal.ToString("yyyy-MM-dd"),
            ["tickets"] = totalTickets,
            ["distinct_users"] = distinctUsers,
            ["pot"] = pot,
            ["fee"] = fee,
            ["payout"] = payout,
            ["winner_id"] = winner.Value.UserId,
            ["winner_tickets"] = winnerTickets,
        });
        LogSettled(row.Id, row.ChatId, totalTickets, pot, payout, winner.Value.UserId);

        return new DailySettleResult(
            Drawn: true, Row: row, TicketsTotal: totalTickets, DistinctUsers: distinctUsers,
            PotTotal: pot, Fee: fee, Payout: payout,
            WinnerId: winner.Value.UserId, WinnerName: winner.Value.DisplayName,
            WinnerTicketCount: winnerTickets);
    }

    // ── history ──────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<PickDailyLotteryRow>> HistoryAsync(long chatId, int limit, CancellationToken ct)
    {
        var bounded = Math.Clamp(limit, 1, 30);
        return store.ListHistoryAsync(chatId, bounded, ct);
    }

    [LoggerMessage(EventId = 5951, Level = LogLevel.Information,
        Message = "pick.daily.buy user={UserId} chat={ChatId} lottery={LotteryId} count={Count} total={TotalUserTickets}")]
    partial void LogBuy(long userId, long chatId, Guid lotteryId, int count, int totalUserTickets);

    [LoggerMessage(EventId = 5952, Level = LogLevel.Information,
        Message = "pick.daily.cancelled_empty lottery={LotteryId} chat={ChatId}")]
    partial void LogSettleCancelledEmpty(Guid lotteryId, long chatId);

    [LoggerMessage(EventId = 5953, Level = LogLevel.Information,
        Message = "pick.daily.settled lottery={LotteryId} chat={ChatId} tickets={Tickets} pot={Pot} payout={Payout} winner={WinnerId}")]
    partial void LogSettled(Guid lotteryId, long chatId, int tickets, int pot, int payout, long winnerId);
}
