// ─────────────────────────────────────────────────────────────────────────────
// Module-private persistence boundary. Stateless modules don't get an
// IRepository<T> (no aggregate); they append audit rows directly through a
// narrow write-only interface. Dapper keeps us free of DbContext coupling the
// way the live codebase already prefers (see a3457e4 "move to postgres move
// economics to dapper").
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Dice;

public interface IDiceHistoryStore
{
    Task AppendAsync(DiceRoll roll, CancellationToken ct);
}

public sealed class DiceHistoryStore(INpgsqlConnectionFactory connections) : IDiceHistoryStore
{
    public async Task AppendAsync(DiceRoll roll, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO dice_rolls (id, user_id, dice_value, prize, loss, rolled_at)
            VALUES (@Id, @UserId, @DiceValue, @Prize, @Loss, @RolledAt)
            """,
            new
            {
                roll.Id,
                roll.UserId,
                roll.DiceValue,
                roll.Prize,
                roll.Loss,
                roll.RolledAt,
            },
            cancellationToken: ct));
    }
}

public sealed record DiceRoll(
    Guid Id,
    long UserId,
    int DiceValue,
    int Prize,
    int Loss,
    DateTimeOffset RolledAt);
