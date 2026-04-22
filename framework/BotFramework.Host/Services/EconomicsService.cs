// ─────────────────────────────────────────────────────────────────────────────
// EconomicsService — the shared ledger. One wallet per (Telegram user,
// balance scope). Scope is usually the chat where play happens so group and DM
// balances stay separate.
//
// Concurrency: every mutation acquires a row lock via SELECT FOR UPDATE. Every
// successful mutation appends a row to economics_ledger in the same transaction.
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

    public async Task EnsureUserAsync(long userId, long balanceScopeId, string displayName, CancellationToken ct)
    {
        var cacheKey = $"user_exists:{userId}:{balanceScopeId}";
        if (cache.TryGetValue(cacheKey, out _)) return;

        if (displayName.Length > 64) displayName = displayName[..64];

        const string sql = """
            INSERT INTO users (telegram_user_id, balance_scope_id, display_name, coins)
            VALUES (@userId, @balanceScopeId, @displayName, @startingCoins)
            ON CONFLICT (telegram_user_id, balance_scope_id)
            DO UPDATE SET display_name = EXCLUDED.display_name, updated_at = now()
            """;

        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            userId,
            balanceScopeId,
            displayName,
            startingCoins = _startingCoins,
        }, cancellationToken: ct));

        cache.Set(cacheKey, true, UserExistsTtl);
    }

    public async Task<int> GetBalanceAsync(long userId, long balanceScopeId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT coins FROM users WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId",
            new { userId, balanceScopeId }, cancellationToken: ct));
    }

    public async Task<bool> TryDebitAsync(
        long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return true;

        var result = await ApplyAsync(userId, balanceScopeId, delta: -amount, allowNegative: false, reason, ct);
        if (result.Applied)
        {
            LogDebit(userId, balanceScopeId, amount, result.NewBalance, reason);
            return true;
        }
        LogDebitRejected(userId, balanceScopeId, amount, result.NewBalance, reason);
        return false;
    }

    public async Task DebitAsync(
        long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        if (!await TryDebitAsync(userId, balanceScopeId, amount, reason, ct))
        {
            var current = await GetBalanceAsync(userId, balanceScopeId, ct);
            throw new InsufficientFundsException(userId, balanceScopeId, amount, current);
        }
    }

    public async Task CreditAsync(
        long userId, long balanceScopeId, int amount, string reason, CancellationToken ct)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount == 0) return;

        var result = await ApplyAsync(userId, balanceScopeId, delta: amount, allowNegative: true, reason, ct);
        LogCredit(userId, balanceScopeId, amount, result.NewBalance, reason);
    }

    public async Task AdjustUncheckedAsync(
        long userId, long balanceScopeId, int delta, CancellationToken ct)
    {
        if (delta == 0) return;
        var result = await ApplyAsync(
            userId, balanceScopeId, delta, allowNegative: true, "admin.adjust", ct);
        LogAdjustUnchecked(userId, balanceScopeId, delta, result.NewBalance);
    }

    private async Task<(bool Applied, int NewBalance)> ApplyAsync(
        long userId, long balanceScopeId, int delta, bool allowNegative, string reason, CancellationToken ct)
    {
        const string selectSql = """
            SELECT coins, version FROM users
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            FOR UPDATE
            """;
        const string updateSql = """
            UPDATE users
            SET coins = @newCoins, version = @newVersion, updated_at = now()
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;
        const string insertLedger = """
            INSERT INTO economics_ledger (telegram_user_id, balance_scope_id, delta, balance_after, reason)
            VALUES (@userId, @balanceScopeId, @delta, @newCoins, @reason)
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<(int coins, long version)>(
            new CommandDefinition(
                selectSql, new { userId, balanceScopeId }, transaction: tx, cancellationToken: ct));

        if (row.Equals(default((int, long))))
        {
            await tx.RollbackAsync(ct);
            throw new InvalidOperationException(
                $"User {userId} scope {balanceScopeId} not found. Call EnsureUserAsync before any balance mutation.");
        }

        var newCoins = row.coins + delta;
        if (!allowNegative && newCoins < 0)
        {
            await tx.RollbackAsync(ct);
            return (false, row.coins);
        }

        var newVersion = row.version + 1;
        await conn.ExecuteAsync(new CommandDefinition(
            updateSql, new { userId, balanceScopeId, newCoins, newVersion }, transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            insertLedger,
            new { userId, balanceScopeId, delta, newCoins, reason },
            transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (true, newCoins);
    }

    [LoggerMessage(LogLevel.Information, "economics.credit user={UserId} scope={BalanceScopeId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogCredit(
        long userId, long balanceScopeId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Information, "economics.debit user={UserId} scope={BalanceScopeId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebit(
        long userId, long balanceScopeId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Warning, "economics.debit_rejected user={UserId} scope={BalanceScopeId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebitRejected(
        long userId, long balanceScopeId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Warning, "economics.adjust_unchecked user={UserId} scope={BalanceScopeId} delta={Delta} balance={Balance}")]
    partial void LogAdjustUnchecked(
        long userId, long balanceScopeId, int delta, int balance);
}
