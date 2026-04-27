using BotFramework.Host;
using Dapper;

namespace BotFramework.Host.Services;

internal sealed class PostgresDistributedGameLock(INpgsqlConnectionFactory connections) : IDistributedGameLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var lockId = HashLockKey(resource);
        var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(@lockId)",
            new { lockId },
            cancellationToken: ct));
        return new Handle(conn, lockId);
    }

    private static long HashLockKey(string key)
    {
        const long offset = unchecked((long)1469598103934665603UL);
        const long prime = 1099511628211;
        long hash = offset;
        foreach (var ch in key)
        {
            hash ^= ch;
            hash *= prime;
        }
        return hash;
    }

    private sealed class Handle(Npgsql.NpgsqlConnection connection, long lockId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "SELECT pg_advisory_unlock(@lockId)",
                    new { lockId }));
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
