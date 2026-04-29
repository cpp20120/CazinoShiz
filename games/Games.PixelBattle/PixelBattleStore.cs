using System.Globalization;
using BotFramework.Host;
using Dapper;

namespace Games.PixelBattle;

public sealed class PixelBattleStore(INpgsqlConnectionFactory connections) : IPixelBattleStore
{
    public async Task<PixelBattleGrid> GetGridAsync(CancellationToken ct)
    {
        var tiles = Enumerable.Repeat(PixelBattleConstants.DefaultColor, PixelBattleConstants.Width * PixelBattleConstants.Height)
            .ToArray();
        var versionstamps = Enumerable.Repeat("", PixelBattleConstants.Width * PixelBattleConstants.Height).ToArray();

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<TileRow>(new CommandDefinition("""
            SELECT index, color, version
            FROM pixelbattle_tiles
            WHERE index >= 0 AND index < @maxIndex
            """, new { maxIndex = PixelBattleConstants.Width * PixelBattleConstants.Height }, cancellationToken: ct));

        foreach (var row in rows)
        {
            tiles[row.Index] = row.Color;
            versionstamps[row.Index] = FormatVersion(row.Version);
        }

        return new PixelBattleGrid(tiles, versionstamps);
    }

    public async Task<PixelBattleUpdate> UpdateTileAsync(int index, string color, long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var version = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            WITH next_version AS (
                SELECT nextval('pixelbattle_version_seq')::bigint AS value
            )
            INSERT INTO pixelbattle_tiles (index, color, version, updated_by, updated_at)
            VALUES (@index, @color, (SELECT value FROM next_version), @userId, now())
            ON CONFLICT (index) DO UPDATE
            SET color = EXCLUDED.color,
                version = EXCLUDED.version,
                updated_by = EXCLUDED.updated_by,
                updated_at = now()
            RETURNING version
            """, new { index, color, userId }, cancellationToken: ct));

        return new PixelBattleUpdate(index, color, FormatVersion(version));
    }

    public async Task<bool> IsKnownUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1
                FROM users
                WHERE telegram_user_id = @userId
                LIMIT 1
            )
            """, new { userId }, cancellationToken: ct));
    }

    private static string FormatVersion(long version) => version.ToString("D20", CultureInfo.InvariantCulture);

    private sealed record TileRow(int Index, string Color, long Version);
}
