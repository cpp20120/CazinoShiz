// ─────────────────────────────────────────────────────────────────────────────
// NpgsqlConnectionFactory — single place that knows how to open a connection
// to the Host's Postgres. Modules ask for one during Dapper-based queries
// (event store, module migrations, ad-hoc reads) without each module bundling
// its own connection-string lookup.
//
// Kept intentionally thin: one sync Create() and one async OpenAsync(). Pool
// tuning is Npgsql's job via the connection string; nothing to configure here.
//
// Bound from the "ConnectionStrings:Default" section the same way ASP.NET
// apps conventionally bind. The Host distribution sets the key in
// appsettings.json (or env via ConnectionStrings__Default) — same shape a
// .NET developer expects.
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
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Default is not set. Configure Postgres connection before starting the bot.");

    public NpgsqlConnection Create() => new(_connectionString);

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = Create();
        await conn.OpenAsync(ct);
        return conn;
    }
}
