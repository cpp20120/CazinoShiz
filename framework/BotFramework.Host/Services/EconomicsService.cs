// ─────────────────────────────────────────────────────────────────────────────
// EconomicsService — the shared ledger. Single source of truth for every
// module's balance operations.
//
// Ported from src/CasinoShiz.Core/Services/Economics/EconomicsService.cs. Key
// differences in the framework version:
//
//   • Pure Dapper/Npgsql — no EF Core coupling. The framework owns the `users`
//     table (see FrameworkMigrations 003_users) so modules never import this
//     entity type.
//   • Operates on raw userId instead of an entity reference. Callers don't
//     need to fetch UserState first; TryDebitAsync / CreditAsync look the row
//     up themselves inside a SELECT … FOR UPDATE transaction.
//   • EnsureUserAsync is first-class; it's the only place the starting-coins
//     seed from BotFrameworkOptions.StartingCoins is applied. Modules call it
//     once per update in their handler entrypoint.
//
// Concurrency: every mutation acquires a row lock via SELECT FOR UPDATE, so
// concurrent Debit/Credit on the same user serialize cleanly in Postgres
// without optimistic-retry gymnastics. The `version` column is kept for
// analytics/audit use, not concurrency control.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BotFramework.Host.Services;

public sealed partial class EconomicsService(
    INpgsqlConnectionFactory connections,
    IOptions<BotFrameworkOptions> options,
    IMemoryCache cache,
    ILogger<EconomicsService> logger) : IEconomicsService
{
    private readonly int _startingCoins = options.Value.StartingCoins;
    private static readonly TimeSpan UserExistsTtl = TimeSpan.FromHours(24);

    public async Task EnsureUserAsync(long userId, string displayName, CancellationToken ct)
    {
        var cacheKey = $"user_exists:{userId}";
        if (cache.TryGetValue(cacheKey, out _)) return;

        const string sql = """
            INSERT INTO users (telegram_user_id, display_name, coins)
            VALUES (@userId, @displayName, @startingCoins)
            ON CONFLICT (telegram_user_id)
            DO UPDATE SET display_name = EXCLUDED.display_name, updated_at = now()
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            userId,
            displayName,
            startingCoins = _startingCoins,
        }, cancellationToken: ct));

        cache.Set(cacheKey, true, UserExistsTtl);
    }

    public async Task<int> GetBalanceAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT coins FROM users WHERE telegram_user_id = @userId",
            new { userId }, cancellationToken: ct));
    }

    public async Task<bool> TryDebitAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return true;

        var result = await ApplyAsync(userId, delta: -amount, allowNegative: false, ct);
        if (result.Applied)
        {
            LogDebit(userId, amount, result.NewBalance, reason);
            return true;
        }
        LogDebitRejected(userId, amount, result.NewBalance, reason);
        return false;
    }

    public async Task DebitAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        if (!await TryDebitAsync(userId, amount, reason, ct))
        {
            var current = await GetBalanceAsync(userId, ct);
            throw new InsufficientFundsException(userId, amount, current);
        }
    }

    public async Task CreditAsync(long userId, int amount, string reason, CancellationToken ct)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return;

        var result = await ApplyAsync(userId, delta: amount, allowNegative: true, ct);
        LogCredit(userId, amount, result.NewBalance, reason);
    }

    private async Task<(bool Applied, int NewBalance)> ApplyAsync(
        long userId, int delta, bool allowNegative, CancellationToken ct)
    {
        const string selectSql = """
            SELECT coins, version FROM users
            WHERE telegram_user_id = @userId
            FOR UPDATE
            """;
        const string updateSql = """
            UPDATE users
            SET coins = @newCoins, version = @newVersion, updated_at = now()
            WHERE telegram_user_id = @userId
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<(int coins, long version)>(
            new CommandDefinition(selectSql, new { userId }, transaction: tx, cancellationToken: ct));

        if (row.Equals(default((int, long))))
            throw new InvalidOperationException(
                $"User {userId} not found. Call EnsureUserAsync before any balance mutation.");

        var newCoins = row.coins + delta;
        if (!allowNegative && newCoins < 0)
        {
            await tx.RollbackAsync(ct);
            return (false, row.coins);
        }

        var newVersion = row.version + 1;
        await conn.ExecuteAsync(new CommandDefinition(updateSql, new
        {
            userId, newCoins, newVersion,
        }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (true, newCoins);
    }

    [LoggerMessage(LogLevel.Information, "economics.credit user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogCredit(long userId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Information, "economics.debit user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebit(long userId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Warning, "economics.debit_rejected user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebitRejected(long userId, int amount, int balance, string reason);
}
