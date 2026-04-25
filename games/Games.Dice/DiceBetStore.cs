// ─────────────────────────────────────────────────────────────────────────────
// DiceBetStore — Dapper-backed persistence for pending dice bets. Allows
// two-phase commit: place bet (debit), then resolve (credit). If SendDice fails,
// the bet can be aborted (refunded). Matches the DiceCubeService pattern.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Dice;

public interface IDiceBetStore
{
    Task<DiceBet?> FindAsync(long userId, long chatId, CancellationToken ct);
    Task<bool> InsertAsync(DiceBet bet, CancellationToken ct);
    Task DeleteAsync(long userId, long chatId, CancellationToken ct);
}

public sealed class DiceBetStore(INpgsqlConnectionFactory connections) : IDiceBetStore
{
    public async Task<DiceBet?> FindAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DiceBet>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, dice_value AS DiceValue, loss AS Loss, created_at AS CreatedAt
            FROM dice_bets WHERE user_id = @userId AND chat_id = @chatId
            """,
            new { userId, chatId },
            cancellationToken: ct));
    }

    public async Task<bool> InsertAsync(DiceBet bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dice_bets (user_id, chat_id, dice_value, loss, created_at)
            VALUES (@UserId, @ChatId, @DiceValue, @Loss, @CreatedAt)
            ON CONFLICT (user_id, chat_id) DO NOTHING
            """,
            bet,
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task DeleteAsync(long userId, long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dice_bets WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId },
            cancellationToken: ct));
    }
}

public sealed record DiceBet(
    long UserId,
    long ChatId,
    int DiceValue,
    int Loss,
    DateTimeOffset CreatedAt);
