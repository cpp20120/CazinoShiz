// ─────────────────────────────────────────────────────────────────────────────
// EfRepository — generic classical-aggregate persistence over EF Core.
//
// Intended use: a module declares its own DbContext (e.g. PokerDbContext) that
// owns the module's tables. The module then wires each classical aggregate:
//
//   services.AddDbContext<PokerDbContext>(o => o.UseNpgsql(...));
//   services.AddScoped<IRepository<PokerTable>, EfRepository<PokerTable, PokerDbContext>>();
//
// One repo per (aggregate, context) pair keeps EF change tracking inside the
// scope where the DbContext lives. Repos are Scoped — never Singleton — so a
// DbContext is reused across one logical unit of work (one Telegram update,
// one admin HTTP request) and disposed at the end.
//
// Why not a single EfRepository<TAggregate> that finds the DbContext for you:
//   EF doesn't expose "which context owns this entity" at runtime without
//   inspecting every registered context. Explicit is cheaper and clearer.
//
// SaveAsync is an upsert: if a row with the same Id exists it's patched with
// CurrentValues.SetValues, otherwise Add. That mirrors the live bot's Dapper-
// based upserts and avoids the "attach then mark modified" dance most EF
// tutorials start with. For more sophisticated behaviour (detecting deletes,
// child collections) a module should ship its own repository that overrides
// SaveAsync.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.EntityFrameworkCore;

namespace BotFramework.Host;

public sealed class EfRepository<TAggregate, TContext>(TContext db) : IRepository<TAggregate>
    where TAggregate : class, IAggregateRoot
    where TContext : DbContext
{
    public async Task<TAggregate?> FindAsync(string id, CancellationToken ct)
    {
        return await db.Set<TAggregate>().FindAsync([id], ct);
    }

    public async Task SaveAsync(TAggregate aggregate, CancellationToken ct)
    {
        var existing = await db.Set<TAggregate>().FindAsync([aggregate.Id], ct);
        if (existing is null)
        {
            db.Set<TAggregate>().Add(aggregate);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(aggregate);
        }
        await db.SaveChangesAsync(ct);
    }
}
