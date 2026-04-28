using BotFramework.Host.Composition;
using Dapper;

namespace BotFramework.Host.Services;

public sealed partial class DailyBonusService(
    INpgsqlConnectionFactory connections,
    IEconomicsService economics,
    IRuntimeTuningAccessor tuning,
    IAnalyticsService analytics,
    ILogger<DailyBonusService> logger) : IDailyBonusService
{
    public async Task<DailyBonusClaimResult> TryClaimAsync(
        long userId, long balanceScopeId, string displayName, CancellationToken ct)
    {
        var opt = tuning.DailyBonus;
        if (!opt.Enabled) return new DailyBonusClaimResult(DailyBonusClaimStatus.Disabled);

        await economics.EnsureUserAsync(userId, balanceScopeId, displayName, ct);
        var today = TodayInOffset(opt.TimezoneOffsetHours);
        var todayDb = today.ToDateTime(TimeOnly.MinValue);

        const string selectSql = """
            SELECT u.coins, u.version, u.last_daily_bonus_on
            FROM users u
            WHERE u.telegram_user_id = @userId AND u.balance_scope_id = @balanceScopeId
            FOR UPDATE
            """;
        const string updateSql = """
            UPDATE users
            SET coins = @newCoins,
                version = @newVersion,
                last_daily_bonus_on = @todayDb,
                updated_at = now()
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;
        const string insertLedger = """
            INSERT INTO economics_ledger (telegram_user_id, balance_scope_id, delta, balance_after, reason)
            VALUES (@userId, @balanceScopeId, @delta, @newCoins, 'daily.bonus')
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var row = await conn.QueryFirstOrDefaultAsync<WalletForDailyRow?>(
            new CommandDefinition(
                selectSql, new { userId, balanceScopeId }, transaction: tx, cancellationToken: ct));
        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return new DailyBonusClaimResult(DailyBonusClaimStatus.IneligibleEmptyBalance);
        }

        var coins = row.coins;
        var version = row.version;
        var lastDay = row.last_daily_bonus_on;
        if (lastDay == today)
        {
            await tx.RollbackAsync(ct);
            return new DailyBonusClaimResult(DailyBonusClaimStatus.AlreadyClaimedToday, 0, coins);
        }

        if (coins <= 0)
        {
            await tx.RollbackAsync(ct);
            return new DailyBonusClaimResult(DailyBonusClaimStatus.IneligibleEmptyBalance, 0, 0);
        }

        var bonus = DailyBonusMath.ComputeBonus(coins, opt.PercentOfBalance, opt.MaxBonus);
        if (bonus < 1)
        {
            await tx.RollbackAsync(ct);
            return new DailyBonusClaimResult(
                DailyBonusClaimStatus.IneligiblePercentRoundsToZero, 0, coins);
        }

        var newCoins = coins + bonus;
        var newVersion = version + 1;
        await conn.ExecuteAsync(
            new CommandDefinition(
                updateSql,
                new
                {
                    userId,
                    balanceScopeId,
                    newCoins,
                    newVersion,
                    todayDb,
                },
                transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(
            new CommandDefinition(
                insertLedger,
                new { userId, balanceScopeId, delta = bonus, newCoins },
                transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        analytics.Track("framework", "daily_bonus", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = balanceScopeId,
            ["bonus"] = bonus,
        });
        LogClaim(userId, balanceScopeId, bonus, newCoins);
        return new DailyBonusClaimResult(DailyBonusClaimStatus.Claimed, bonus, newCoins);
    }

    private static DateOnly TodayInOffset(int hoursEastOfUtc)
    {
        var instant = DateTimeOffset.UtcNow;
        var shifted = instant.AddHours(hoursEastOfUtc);
        return DateOnly.FromDateTime(shifted.DateTime);
    }

    private sealed class WalletForDailyRow
    {
        public int coins { get; init; }
        public long version { get; init; }
        public DateOnly? last_daily_bonus_on { get; init; }
    }

    [LoggerMessage(LogLevel.Information, "daily_bonus.credited user={UserId} scope={Scope} bonus={Bonus} balance={Balance}")]
    partial void LogClaim(long userId, long scope, int bonus, int balance);
}
