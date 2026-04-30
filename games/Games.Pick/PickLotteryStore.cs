// ─────────────────────────────────────────────────────────────────────────────
// PickLotteryStore — Dapper queries for the /picklottery game.
//
// Locking strategy:
//   • Open / join writes go through a transaction with FOR UPDATE on the
//     pick_lottery row to keep entry counts and refund states consistent.
//   • The unique partial index ux_pick_lottery_open_per_chat is the canonical
//     guarantee that there is at most one open pool per chat — race-free at
//     the DB level, no application-side locking required.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Pick;

public sealed record PickLotteryRow(
    Guid Id,
    long ChatId,
    long OpenerId,
    string OpenerName,
    int Stake,
    string Status,
    DateTime OpenedAt,
    DateTime DeadlineAt,
    DateTime? SettledAt,
    long? WinnerId,
    string? WinnerName,
    int? PotTotal,
    int? Payout,
    int? Fee);

public sealed record PickLotteryEntryRow(
    Guid LotteryId,
    long UserId,
    string DisplayName,
    int StakePaid,
    DateTime EnteredAt);

public enum LotteryOpenError
{
    None,
    AlreadyOpenInChat,
    DbError,
}

public enum LotteryJoinError
{
    None,
    NoOpenLottery,
    AlreadyJoined,
    DbError,
}

public interface IPickLotteryStore
{
    Task<(LotteryOpenError Err, PickLotteryRow? Row)> InsertOpenAsync(
        Guid id, long chatId, long openerId, string openerName,
        int stake, DateTime deadlineUtc, CancellationToken ct);

    Task<PickLotteryRow?> FindOpenByChatAsync(long chatId, CancellationToken ct);

    Task<PickLotteryRow?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<(LotteryJoinError Err, PickLotteryRow? Row)> AddEntryAsync(
        long chatId, long userId, string displayName, CancellationToken ct);

    Task<IReadOnlyList<PickLotteryEntryRow>> ListEntriesAsync(Guid lotteryId, CancellationToken ct);

    /// <summary>Returns up to N expired open pools for the sweeper.</summary>
    Task<IReadOnlyList<PickLotteryRow>> ListExpiredOpenAsync(int limit, CancellationToken ct);

    /// <summary>Atomically transitions an open pool to settled with the chosen winner.</summary>
    Task<bool> MarkSettledAsync(
        Guid id, long winnerId, string winnerName,
        int potTotal, int payout, int fee, CancellationToken ct);

    /// <summary>Atomically transitions an open pool to cancelled (no winner).</summary>
    Task<bool> MarkCancelledAsync(Guid id, CancellationToken ct);
}

public sealed class PickLotteryStore(INpgsqlConnectionFactory connections) : IPickLotteryStore
{
    public async Task<(LotteryOpenError Err, PickLotteryRow? Row)> InsertOpenAsync(
        Guid id, long chatId, long openerId, string openerName,
        int stake, DateTime deadlineUtc, CancellationToken ct)
    {
        const string insertLotterySql = """
            INSERT INTO pick_lottery (id, chat_id, opener_id, opener_name, stake, status, deadline_at)
            VALUES (@id, @chatId, @openerId, @openerName, @stake, 'open', @deadlineUtc)
            ON CONFLICT DO NOTHING
            RETURNING id, chat_id, opener_id, opener_name, stake, status,
                      opened_at, deadline_at, settled_at,
                      winner_id, winner_name, pot_total, payout, fee
            """;
        const string insertOpenerEntrySql = """
            INSERT INTO pick_lottery_entries (lottery_id, user_id, display_name, stake_paid)
            VALUES (@id, @openerId, @openerName, @stake)
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var row = await conn.QueryFirstOrDefaultAsync<PickLotteryRow?>(
                new CommandDefinition(insertLotterySql, new
                {
                    id, chatId, openerId, openerName, stake, deadlineUtc,
                }, transaction: tx, cancellationToken: ct));

            if (row is null)
            {
                await tx.RollbackAsync(ct);
                return (LotteryOpenError.AlreadyOpenInChat, null);
            }

            await conn.ExecuteAsync(new CommandDefinition(insertOpenerEntrySql,
                new { id, openerId, openerName, stake },
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return (LotteryOpenError.None, row);
        }
        catch (Npgsql.PostgresException pe) when (pe.SqlState == "23505")
        {
            // ux_pick_lottery_open_per_chat — concurrent /picklottery in same chat.
            await tx.RollbackAsync(ct);
            return (LotteryOpenError.AlreadyOpenInChat, null);
        }
    }

    public async Task<PickLotteryRow?> FindOpenByChatAsync(long chatId, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, opener_id, opener_name, stake, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, pot_total, payout, fee
            FROM pick_lottery
            WHERE chat_id = @chatId AND status = 'open'
            LIMIT 1
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<PickLotteryRow?>(
            new CommandDefinition(sql, new { chatId }, cancellationToken: ct));
    }

    public async Task<PickLotteryRow?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, opener_id, opener_name, stake, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, pot_total, payout, fee
            FROM pick_lottery
            WHERE id = @id
            """;
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<PickLotteryRow?>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<(LotteryJoinError Err, PickLotteryRow? Row)> AddEntryAsync(
        long chatId, long userId, string displayName, CancellationToken ct)
    {
        const string lockSql = """
            SELECT id, chat_id, opener_id, opener_name, stake, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, pot_total, payout, fee
            FROM pick_lottery
            WHERE chat_id = @chatId AND status = 'open'
            FOR UPDATE
            """;
        const string insertEntrySql = """
            INSERT INTO pick_lottery_entries (lottery_id, user_id, display_name, stake_paid)
            VALUES (@lotteryId, @userId, @displayName, @stake)
            ON CONFLICT (lottery_id, user_id) DO NOTHING
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var lottery = await conn.QueryFirstOrDefaultAsync<PickLotteryRow?>(
            new CommandDefinition(lockSql, new { chatId }, transaction: tx, cancellationToken: ct));
        if (lottery is null)
        {
            await tx.RollbackAsync(ct);
            return (LotteryJoinError.NoOpenLottery, null);
        }

        var inserted = await conn.ExecuteAsync(new CommandDefinition(
            insertEntrySql,
            new { lotteryId = lottery.Id, userId, displayName, stake = lottery.Stake },
            transaction: tx, cancellationToken: ct));

        if (inserted == 0)
        {
            await tx.RollbackAsync(ct);
            return (LotteryJoinError.AlreadyJoined, lottery);
        }

        await tx.CommitAsync(ct);
        return (LotteryJoinError.None, lottery);
    }

    public async Task<IReadOnlyList<PickLotteryEntryRow>> ListEntriesAsync(Guid lotteryId, CancellationToken ct)
    {
        const string sql = """
            SELECT lottery_id  AS LotteryId,
                   user_id     AS UserId,
                   display_name AS DisplayName,
                   stake_paid  AS StakePaid,
                   entered_at  AS EnteredAt
            FROM pick_lottery_entries
            WHERE lottery_id = @lotteryId
            ORDER BY entered_at
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PickLotteryEntryRow>(
            new CommandDefinition(sql, new { lotteryId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PickLotteryRow>> ListExpiredOpenAsync(int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT id, chat_id, opener_id, opener_name, stake, status,
                   opened_at, deadline_at, settled_at,
                   winner_id, winner_name, pot_total, payout, fee
            FROM pick_lottery
            WHERE status = 'open' AND deadline_at <= now()
            ORDER BY deadline_at
            LIMIT @limit
            """;
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PickLotteryRow>(
            new CommandDefinition(sql, new { limit }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> MarkSettledAsync(
        Guid id, long winnerId, string winnerName,
        int potTotal, int payout, int fee, CancellationToken ct)
    {
        const string sql = """
            UPDATE pick_lottery
            SET status = 'settled',
                settled_at = now(),
                winner_id = @winnerId,
                winner_name = @winnerName,
                pot_total = @potTotal,
                payout = @payout,
                fee = @fee
            WHERE id = @id AND status = 'open'
            """;
        await using var conn = await connections.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            sql, new { id, winnerId, winnerName, potTotal, payout, fee }, cancellationToken: ct));
        return n > 0;
    }

    public async Task<bool> MarkCancelledAsync(Guid id, CancellationToken ct)
    {
        const string sql = """
            UPDATE pick_lottery
            SET status = 'cancelled',
                settled_at = now()
            WHERE id = @id AND status = 'open'
            """;
        await using var conn = await connections.OpenAsync(ct);
        var n = await conn.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return n > 0;
    }
}
