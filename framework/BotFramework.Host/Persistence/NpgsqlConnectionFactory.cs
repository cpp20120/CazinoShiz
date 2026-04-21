// ─────────────────────────────────────────────────────────────────────────────
// NpgsqlConnectionFactory — single place that knows how to open a connection
// to the Host's Postgres. Modules ask for one during Dapper-based queries
// (event store, module migrations, ad-hoc reads) without each module bundling
// its own connection-string lookup.
//
// Kept intentionally thin: one sync Create() and one async OpenAsync(). Pool
// tuning is Npgsql's job via the connection string; nothing to configure here.
//
// Reads "ConnectionStrings:Postgres" (preferred) falling back to "Default"
// for backwards compat. Set via ConnectionStrings__Postgres env var.
// ─────────────────────────────────────────────────────────────────────────────

using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BotFramework.Host;

public interface INpgsqlConnectionFactory
{
    NpgsqlConnection Create();
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct);
}

public sealed class NpgsqlConnectionFactory(IConfiguration configuration) : INpgsqlConnectionFactory
{
    private readonly string _connectionString =
        configuration.GetConnectionString("Postgres")
        ?? configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Postgres is not set. Configure Postgres connection before starting the bot.");

    public NpgsqlConnection Create() => new(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = Create();
        await conn.OpenAsync(ct);
        return conn;
    }
}
