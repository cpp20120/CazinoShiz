// ─────────────────────────────────────────────────────────────────────────────
// ModuleMigrationRunner — applies each module's migrations against the shared
// __module_migrations tracking table at Host startup.
//
// Registered as the FIRST IHostedService so schemas are ready before polling
// starts or any projection queries fire. StartAsync is synchronous-from-DI's
// perspective: the generic host awaits it before starting later services.
//
// Ordering:
//   1. Host migrations (framework schema: module_events, module_snapshots,
//      tracking table itself). Bundled under module id "_framework".
//   2. Per-module migrations, in module-registration order. Since modules
//      don't share schema, ordering between modules doesn't matter for
//      correctness — but sticking with registration order gives deterministic
//      logs, which operators will thank us for.
//
// Safety checks on every apply:
//   • unique (module_id, migration_id) — dupes are rejected by PK
//   • content hash matches what's in the tracking row — someone edited an
//     applied migration → startup fails loud, before anything else runs
//
// Transaction shape: each migration is its own transaction. The tracking-row
// insert lives in the same transaction as the DDL, so a failing migration
// leaves no phantom tracking row.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Dapper;

namespace BotFramework.Host.Composition;

public sealed partial class ModuleMigrationRunner(
    INpgsqlConnectionFactory connections,
    LoadedModules loadedModules,
    ILogger<ModuleMigrationRunner> logger) : IHostedService
{
    private const string EnsureTrackingTable = """
        CREATE TABLE IF NOT EXISTS __module_migrations (
            module_id      TEXT         NOT NULL,
            migration_id   TEXT         NOT NULL,
            content_hash   TEXT         NOT NULL,
            applied_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
            PRIMARY KEY (module_id, migration_id)
        );
        """;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var conn = await connections.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(new CommandDefinition(EnsureTrackingTable, cancellationToken: cancellationToken));

        // Framework-owned schema first (module_events / module_snapshots tables).
        await ApplyModuleAsync(new FrameworkMigrations(), cancellationToken);

        foreach (var module in loadedModules.Migrations)
            await ApplyModuleAsync(module, cancellationToken);

        LogMigrationsComplete(loadedModules.Migrations.Count + 1);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ApplyModuleAsync(IModuleMigrations module, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var applied = await LoadAppliedAsync(conn, module.ModuleId, ct);

        foreach (var migration in module.Migrations)
        {
            if (applied.TryGetValue(migration.Id, out var appliedHash))
            {
                if (appliedHash != migration.ContentHash)
                    throw new InvalidOperationException(
                        $"migration {module.ModuleId}:{migration.Id} was edited after apply " +
                        $"(hash {appliedHash} → {migration.ContentHash}). " +
                        "Write a new forward migration instead of editing an applied one.");
                continue;
            }

            LogApplying(module.ModuleId, migration.Id);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await conn.ExecuteAsync(new CommandDefinition(migration.Sql, transaction: tx, cancellationToken: ct));
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO __module_migrations (module_id, migration_id, content_hash) VALUES (@m, @i, @h)",
                    new { m = module.ModuleId, i = migration.Id, h = migration.ContentHash },
                    transaction: tx,
                    cancellationToken: ct));
                await tx.CommitAsync(ct);
                LogApplied(module.ModuleId, migration.Id);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }

    private static async Task<Dictionary<string, string>> LoadAppliedAsync(
        Npgsql.NpgsqlConnection conn, string moduleId, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<(string migration_id, string content_hash)>(
            new CommandDefinition(
                "SELECT migration_id, content_hash FROM __module_migrations WHERE module_id = @moduleId",
                new { moduleId },
                cancellationToken: ct));
        return rows.ToDictionary(r => r.migration_id, r => r.content_hash);
    }

    [LoggerMessage(EventId = 1500, Level = LogLevel.Information, Message = "migration.applying module={ModuleId} id={MigrationId}")]
    partial void LogApplying(string moduleId, string migrationId);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Information, Message = "migration.applied module={ModuleId} id={MigrationId}")]
    partial void LogApplied(string moduleId, string migrationId);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information, Message = "migration.complete module_count={Count}")]
    partial void LogMigrationsComplete(int count);
}
