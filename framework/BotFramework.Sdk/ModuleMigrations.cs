// ─────────────────────────────────────────────────────────────────────────────
// Per-module migrations.
//
// Earlier sketch iteration: shared AppDbContext + IModule.ConfigureEntities
// where every module registered its entities into one model, and the Host's
// single EF migration chain covered everything. That was the pragmatic
// tradeoff because EF Core dislikes plugin-owned schemas.
//
// Updated tradeoff: modules own their schema.
//
// Each module ships IModuleMigrations — an ordered list of SQL migrations with
// stable IDs. The Host runs migrations per-module against a dedicated
// tracking table `__module_migrations_<moduleId>`. Two modules can't collide
// on migration IDs because tracking is namespaced.
//
// Why this works:
//   • the only shared schema is module_events, module_snapshots, and
//     __module_migrations_* — all Host-owned, all known up front
//   • modules read/write their own tables via Dapper (already the project's
//     direction; see a3457e4 "move to postgres move economics to dapper")
//   • no DbContext coupling means a module can ship as a nuget package
//     without pulling EF into every consumer's dependency graph
//
// Why we didn't pick "EF migrations per-module DbContext":
//   pulls Microsoft.EntityFrameworkCore.Design into every module project,
//   doubles the scaffolding cost, and the migration artifacts are generated
//   C# files that bloat the module repo. Hand-rolled SQL in a single file
//   per migration is faster to review and easier to roll back.
//
// Rollback story:
//   migrations are forward-only in this sketch. If you need to roll back,
//   write a new forward migration. Matches how the real codebase already
//   behaves today — EF's "down" methods were never invoked in prod.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IModuleMigrations
{
    /// Stable migration id prefix for this module. The Host prefixes each
    /// migration ID with this to avoid cross-module collisions ("poker:001",
    /// "sh:0042"). In practice the module's IModule.Id is a fine value.
    string ModuleId { get; }

    /// Ordered list of migrations. MUST be append-only: editing a migration
    /// that's already been applied in production is the usual way people
    /// break their schema history. The runner refuses to apply migrations
    /// whose content hash differs from a previously-applied row with the
    /// same id.
    IReadOnlyList<Migration> Migrations { get; }
}

/// A single forward migration. Id is the stable ordering key ("001_initial",
/// "002_add_stack_column"). Sql is executed verbatim — the runner does not
/// try to parse it. For cases where SQL won't do (data migrations, etc.),
/// add a ContentProvider with a Run(IDbConnection) callback — not included
/// in this sketch to keep the surface small.
public sealed record Migration(string Id, string Sql)
{
    /// Used by the runner to detect tampering: if someone edits an applied
    /// migration's SQL, the hash mismatch surfaces at startup before any
    /// damage is done. Override only if you have a good reason.
    public string ContentHash => ComputeHash(Sql);

    private static string ComputeHash(string sql) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql)));
}
