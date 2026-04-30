// ─────────────────────────────────────────────────────────────────────────────
// PickDailyLotteryStore — Dapper queries for the per-chat daily lottery.
//
// Tables:
//   pick_daily_lottery          (chat_id, day_local) UNIQUE, status state machine
//   pick_daily_lottery_tickets  one row per purchased ticket (FK + cascade)
//
// Race-safe ergonomics:
//   • GetOrCreateOpenAsync uses INSERT ... ON CONFLICT DO NOTHING + SELECT
//     so the first concurrent caller wins the insert and the rest just read.
//   • Settle uses UPDATE ... WHERE status='open' so a manually-cancelled row
//     can't double-settle.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Pick;

public sealed record PickDailyLotteryRow(
    Guid Id,
    long ChatId,
    DateTime DayLocal,
    int TicketPrice,
    string Status,
    DateTime OpenedAt,
    DateTime DeadlineAt,
    DateTime? SettledAt,
    long? WinnerId,
    string? WinnerName,
    int? TicketCount,
    int? PotTotal,
    int? Payout,
    int? Fee);

public sealed record PickDailyTicketSummary(long UserId, string DisplayName, int TicketCount);

public interface IPickDailyLotteryStore
{
    Task<PickDailyLotteryRow> GetOrCreateOpenAsync(
        long chatId, DateOnly dayLocal, int ticketPrice, DateTime deadlineUtc, CancellationToken ct);

    Task<PickDailyLotteryRow?> FindOpenByChatAsync(long chatId, DateOnly dayLocal, CancellationToken ct);

    Task<int> InsertTicketsAsync(
        Guid lotteryId, long userId, string displayName, int count, int pricePaid, CancellationToken ct);

    Task<int> CountTicketsAsync(Guid lotteryId, CancellationToken ct);

    Task<int> CountUserTicketsAsync(Guid lotteryId, long userId, CancellationToken ct);

    Task<int> CountDistinctUsersAsync(Guid lotteryId, CancellationToken ct);

    Task<IReadOnlyList<PickDailyTicketSummary>> ListUserTicketCountsAsync(
        Guid lotteryId, CancellationToken ct);

    Task<IReadOnlyList<PickDailyLotteryRow>> ListExpiredOpenAsync(int limit, CancellationToken ct);

    /// <summary>Picks a uniformly-random ticket and returns its owner. Null if 0 tickets.</summary>
    Task<(long UserId, string DisplayName)?> PickRandomWinnerAsync(Guid lotteryId, CancellationToken ct);

    Task<bool> MarkSettledAsync(Guid id, long winnerId, string winnerName,
        int ticketCount, int potTotal, int payout, int fee, CancellationToken ct);

    Task<bool> MarkCancelledAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<PickDailyLotteryRow>> ListHistoryAsync(long chatId, int limit, CancellationToken ct);
}

public sealed class PickDailyLotteryStore(INpgsqlConnectionFactory connections) : IPickDailyLotteryStore
{
    public async Task<PickDailyLotteryRow> GetOrCreateOpenAsync(
        long chatId, DateOnly dayLocal, int ticketPrice, DateTime deadlineUtc, CancellationToken ct)
    {
        // Two-step: first try INSERT ON CONFLICT DO NOTHING and grab the new
        // row. If the INSERT was a no-op (someone else won the race), do a
        // plain SELECT for the existing row. Both paths return the same row.
        const string upsertSql = """
            WITH inserted AS (
                INSERT INTO pick_daily_lottery
                       (id, chat_id, day_local, ticket_price, status, deadline_at)
                VALUES (@id, @chatId, @dayLocal, @ticketPrice, 'open', @deadlineUtc)
                ON CONFLICT (chat_id, day_local) DO NOTHING
                RETURNING id, chat_id, day_local, ticket_price, status,
                          opened_at, deadline_at, settled_at,
                          winner_id, winner_name, ticket_count, pot_total, payout, fee
            )
            SELECT * FROM inserted
            UNION ALL
            SELECT id, chat_id, day_local, ticket_price, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, ticket_count, pot_total, payout, fee
            FROM pick_daily_lottery
            WHERE chat_id = @chatId AND day_local = @dayLocal
              AND NOT EXISTS (SELECT 1 FROM inserted)
            LIMIT 1
            """;

        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QueryFirstAsync<PickDailyLotteryRow>(
            new CommandDefinition(upsertSql,
                new
                {
                    id = Guid.NewGuid(),
                    chatId,
                    dayLocal = dayLocal.ToDateTime(TimeOnly.MinValue),
                    ticketPrice,
                    deadlineUtc,
                },
                cancellationToken: ct));
        return row;
    }

    public async Task<PickDailyLotteryRow?> FindOpenByChatAsync(
        long chatId, DateOnly dayLocal, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, day_local, ticket_price, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, ticket_count, pot_total, payout, fee
            FROM pick_daily_lottery
            WHERE chat_id = @chatId AND day_local = @dayLocal AND status = 'open'
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<PickDailyLotteryRow?>(
            new CommandDefinition(sql,
                new { chatId, dayLocal = dayLocal.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: ct));
    }

    public async Task<int> InsertTicketsAsync(
        Guid lotteryId, long userId, string displayName, int count, int pricePaid, CancellationToken ct)
    {
        if (count <= 0) return 0;

        // generate_series gives us N rows in one round-trip — much cheaper
        // than N separate INSERTs for users who buy many tickets.
        const string sql = """
            INSERT INTO pick_daily_lottery_tickets
                   (lottery_id, user_id, display_name, price_paid)
            SELECT @lotteryId, @userId, @displayName, @pricePaid
            FROM generate_series(1, @count)
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteAsync(new CommandDefinition(sql,
            new { lotteryId, userId, displayName, pricePaid, count },
            cancellationToken: ct));
    }

    public async Task<int> CountTicketsAsync(Guid lotteryId, CancellationToken ct)
    {
        const string sql = "SELECT count(*) FROM pick_daily_lottery_tickets WHERE lottery_id = @lotteryId";
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { lotteryId }, cancellationToken: ct));
    }

    public async Task<int> CountUserTicketsAsync(Guid lotteryId, long userId, CancellationToken ct)
    {
        const string sql = """
            SELECT count(*) FROM pick_daily_lottery_tickets
            WHERE lottery_id = @lotteryId AND user_id = @userId
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { lotteryId, userId }, cancellationToken: ct));
    }

    public async Task<int> CountDistinctUsersAsync(Guid lotteryId, CancellationToken ct)
    {
        const string sql = """
            SELECT count(DISTINCT user_id) FROM pick_daily_lottery_tickets
            WHERE lottery_id = @lotteryId
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { lotteryId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PickDailyTicketSummary>> ListUserTicketCountsAsync(
        Guid lotteryId, CancellationToken ct)
    {
        const string sql = """
            SELECT user_id     AS UserId,
                   max(display_name) AS DisplayName,
                   count(*)::int     AS TicketCount
            FROM pick_daily_lottery_tickets
            WHERE lottery_id = @lotteryId
            GROUP BY user_id
            ORDER BY count(*) DESC, max(bought_at) ASC
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PickDailyTicketSummary>(
            new CommandDefinition(sql, new { lotteryId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PickDailyLotteryRow>> ListExpiredOpenAsync(int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, day_local, ticket_price, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, ticket_count, pot_total, payout, fee
            FROM pick_daily_lottery
            WHERE status = 'open' AND deadline_at <= now()
            ORDER BY deadline_at
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PickDailyLotteryRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<(long UserId, string DisplayName)?> PickRandomWinnerAsync(
        Guid lotteryId, CancellationToken ct)
    {
        const string sql = """
            SELECT user_id AS UserId, display_name AS DisplayName
            FROM pick_daily_lottery_tickets
            WHERE lottery_id = @lotteryId
            ORDER BY random()
            LIMIT 1
            """;
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<(long UserId, string DisplayName)?>(
            new CommandDefinition(sql, new { lotteryId }, cancellationToken: ct));
        return row;
    }

    public async Task<bool> MarkSettledAsync(
        Guid id, long winnerId, string winnerName,
        int ticketCount, int potTotal, int payout, int fee, CancellationToken ct)
    {
        const string sql = """
            UPDATE pick_daily_lottery
            SET status       = 'settled',
                settled_at   = now(),
                winner_id    = @winnerId,
                winner_name  = @winnerName,
                ticket_count = @ticketCount,
                pot_total    = @potTotal,
                payout       = @payout,
                fee          = @fee
            WHERE id = @id AND status = 'open'
            """;
        await using var conn = await connections.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { id, winnerId, winnerName, ticketCount, potTotal, payout, fee },
            cancellationToken: ct));
        return n > 0;
    }

    public async Task<bool> MarkCancelledAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            UPDATE pick_daily_lottery
            SET status     = 'cancelled',
                settled_at = now()
            WHERE id = @id AND status = 'open'
            """;
        await using var conn = await connections.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return n > 0;
    }

    public async Task<IReadOnlyList<PickDailyLotteryRow>> ListHistoryAsync(
        long chatId, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, day_local, ticket_price, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, ticket_count, pot_total, payout, fee
            FROM pick_daily_lottery
            WHERE chat_id = @chatId AND status IN ('settled', 'cancelled')
            ORDER BY day_local DESC
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PickDailyLotteryRow>(
            new CommandDefinition(sql, new { chatId, limit }, cancellationToken: ct));
        return rows.ToList();
    }
}
