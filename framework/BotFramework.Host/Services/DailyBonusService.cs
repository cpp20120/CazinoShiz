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
        return await TryCreditDayAsync(userId, balanceScopeId, today, markDay: today, ct);
    }

    public async Task<DailyBonusCatchUpStats> CatchUpMissedDaysAsync(CancellationToken ct)
    {
        var opt = tuning.DailyBonus;
        if (!opt.Enabled || !opt.CatchUpEnabled)
            return new DailyBonusCatchUpStats(0, 0, 0, 0);

        var today = TodayInOffset(opt.TimezoneOffsetHours);
        var maxDays = Math.Clamp(opt.MaxCatchUpDays, 0, 365);
        if (maxDays == 0)
            return new DailyBonusCatchUpStats(0, 0, 0, 0);

        const string sql = """
            SELECT telegram_user_id AS UserId,
                   balance_scope_id AS BalanceScopeId,
                   last_daily_bonus_on AS LastDailyBonusOn
            FROM users
            WHERE coins > 0 AND last_daily_bonus_on IS NOT NULL AND last_daily_bonus_on < @todayDb
            ORDER BY last_daily_bonus_on ASC
            LIMIT 5000
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<CatchUpWalletRow>(new CommandDefinition(
            sql,
            new { todayDb = today.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct));

        var wallets = 0;
        var days = 0;
        var credited = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            wallets++;
            if (row.LastDailyBonusOn is not { } lastDay)
            {
                skipped++;
                continue;
            }

            var start = lastDay.AddDays(1);
            var endExclusive = today;
            var floor = today.AddDays(-maxDays);
            if (start < floor) start = floor;

            for (var day = start; day < endExclusive; day = day.AddDays(1))
            {
                days++;
                var result = await TryCreditDayAsync(row.UserId, row.BalanceScopeId, day, markDay: day, ct);
                if (result.Status == DailyBonusClaimStatus.Claimed)
                    credited += result.BonusCoins;
                else
                    skipped++;
            }
        }

        if (wallets > 0)
            LogCatchUp(wallets, days, credited, skipped);
        return new DailyBonusCatchUpStats(wallets, days, credited, skipped);
    }

    private async Task<DailyBonusClaimResult> TryCreditDayAsync(
        long userId,
        long balanceScopeId,
        DateOnly bonusDay,
        DateOnly markDay,
        CancellationToken ct)
    {
        var opt = tuning.DailyBonus;
        var dayDb = bonusDay.ToDateTime(TimeOnly.MinValue);
        var markDb = markDay.ToDateTime(TimeOnly.MinValue);
        var operationId = $"daily.bonus:{balanceScopeId}:{userId}:{bonusDay:yyyy-MM-dd}";

        const string selectSql = """
            SELECT u.coins, u.version, u.last_daily_bonus_on
            FROM users u
            WHERE u.telegram_user_id = @userId AND u.balance_scope_id = @balanceScopeId
            FOR UPDATE
            """;
        const string existingSql = """
            SELECT balance_after
            FROM economics_ledger
            WHERE operation_id = @operationId
            """;
        const string updateSql = """
            UPDATE users
            SET coins = @newCoins,
                version = @newVersion,
                last_daily_bonus_on = @markDb,
                updated_at = now()
            WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
            """;
        const string insertLedger = """
            INSERT INTO economics_ledger (telegram_user_id, balance_scope_id, delta, balance_after, reason, operation_id)
            VALUES (@userId, @balanceScopeId, @delta, @newCoins, 'daily.bonus', @operationId)
            """;

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var existing = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            existingSql,
            new { operationId },
            transaction: tx,
            cancellationToken: ct));
        if (existing.HasValue)
        {
            await tx.RollbackAsync(ct);
            return new DailyBonusClaimResult(DailyBonusClaimStatus.AlreadyClaimedToday, 0, existing.Value);
        }

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
        if (lastDay.HasValue && lastDay.Value >= bonusDay)
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
                    markDb,
                },
                transaction: tx, cancellationToken: ct));
        await conn.ExecuteAsync(
            new CommandDefinition(
                insertLedger,
                new { userId, balanceScopeId, delta = bonus, newCoins, operationId },
                transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);

        analytics.Track("framework", "daily_bonus", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = balanceScopeId,
            ["bonus"] = bonus,
            ["bonus_day"] = bonusDay.ToString("yyyy-MM-dd"),
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

    private sealed class CatchUpWalletRow
    {
        public long UserId { get; init; }
        public long BalanceScopeId { get; init; }
        public DateOnly? LastDailyBonusOn { get; init; }
    }

    [LoggerMessage(LogLevel.Information, "daily_bonus.credited user={UserId} scope={Scope} bonus={Bonus} balance={Balance}")]
    partial void LogClaim(long userId, long scope, int bonus, int balance);

    [LoggerMessage(LogLevel.Information, "daily_bonus.catchup wallets={Wallets} days={Days} credited={Credited} skipped={Skipped}")]
    partial void LogCatchUp(int wallets, int days, int credited, int skipped);
}