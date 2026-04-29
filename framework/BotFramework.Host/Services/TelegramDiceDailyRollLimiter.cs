using Dapper;
using BotFramework.Host.Composition;
using Microsoft.Extensions.Options;

namespace BotFramework.Host.Services;

internal sealed class TelegramDiceDailyRollLimiter(
    INpgsqlConnectionFactory connections,
    IRuntimeTuningAccessor tuning,
    IOptions<BotFrameworkOptions> botOptions) : ITelegramDiceDailyRollLimiter
{
    public async Task<TelegramDiceRollGateResult> TryConsumeRollAsync(
        long userId, long balanceScopeId, string gameId, CancellationToken ct)
    {
        if (IsPrivateAdminScope(userId, balanceScopeId))
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, 0);

        var o = tuning.TelegramDiceDailyLimit;
        var max = o.GetMaxRollsPerUserPerDay(gameId);
        if (max <= 0)
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, 0);

        var today = TodayInOffset(o.TimezoneOffsetHours);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO telegram_dice_daily_rolls (
                    telegram_user_id,
                    balance_scope_id,
                    game_id,
                    rolls_on,
                    roll_count
                )
                VALUES (@userId, @balanceScopeId, @gameId, @today, 0)
                ON CONFLICT (telegram_user_id, balance_scope_id, game_id) DO NOTHING
                """,
                new { userId, balanceScopeId, gameId, today = today.ToDateTime(TimeOnly.MinValue) },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        var row = await conn.QuerySingleAsync<DiceRollRow>(
            new CommandDefinition(
                """
                SELECT rolls_on AS RollsOn,
                       roll_count AS RollCount
                FROM telegram_dice_daily_rolls
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                FOR UPDATE
                """,
                new { userId, balanceScopeId, gameId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        var count = row.RollsOn == today ? row.RollCount : 0;

        if (count >= max)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.LimitExceeded, count, max);
        }

        var newCount = count + 1;
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE telegram_dice_daily_rolls SET
                    rolls_on = @today,
                    roll_count = @newCount,
                    updated_at = now()
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                """,
                new { userId, balanceScopeId, gameId, today = today.ToDateTime(TimeOnly.MinValue), newCount },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, newCount, max);
    }

    public async Task<TelegramDiceRollGateResult> GetRollStatusAsync(
        long userId, long balanceScopeId, string gameId, CancellationToken ct)
    {
        if (IsPrivateAdminScope(userId, balanceScopeId))
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, 0);

        var o = tuning.TelegramDiceDailyLimit;
        var max = o.GetMaxRollsPerUserPerDay(gameId);
        if (max <= 0)
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, 0);

        var today = TodayInOffset(o.TimezoneOffsetHours);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<DiceRollRow?>(
            new CommandDefinition(
                """
                SELECT rolls_on AS RollsOn,
                       roll_count AS RollCount
                FROM telegram_dice_daily_rolls
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                """,
                new { userId, balanceScopeId, gameId },
                cancellationToken: ct)).ConfigureAwait(false);

        var count = row?.RollsOn == today ? row.RollCount : 0;
        return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, count, max);
    }

    public async Task GrantExtraRollAsync(long userId, long balanceScopeId, string gameId, CancellationToken ct)
    {
        if (IsPrivateAdminScope(userId, balanceScopeId))
            return;

        var o = tuning.TelegramDiceDailyLimit;
        if (o.GetMaxRollsPerUserPerDay(gameId) <= 0) return;

        var today = TodayInOffset(o.TimezoneOffsetHours);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO telegram_dice_daily_rolls (
                    telegram_user_id,
                    balance_scope_id,
                    game_id,
                    rolls_on,
                    roll_count
                )
                VALUES (@userId, @balanceScopeId, @gameId, @today, 0)
                ON CONFLICT (telegram_user_id, balance_scope_id, game_id) DO NOTHING
                """,
                new { userId, balanceScopeId, gameId, today = today.ToDateTime(TimeOnly.MinValue) },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        var row = await conn.QuerySingleAsync<DiceRollRow>(
            new CommandDefinition(
                """
                SELECT rolls_on AS RollsOn,
                       roll_count AS RollCount
                FROM telegram_dice_daily_rolls
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                FOR UPDATE
                """,
                new { userId, balanceScopeId, gameId },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        var count = row.RollsOn == today ? row.RollCount : 0;
        var newCount = count - 1;

        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE telegram_dice_daily_rolls SET
                    rolls_on = @today,
                    roll_count = @newCount,
                    updated_at = now()
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                """,
                new { userId, balanceScopeId, gameId, today = today.ToDateTime(TimeOnly.MinValue), newCount },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task TryRefundRollAsync(long userId, long balanceScopeId, string gameId, CancellationToken ct)
    {
        if (IsPrivateAdminScope(userId, balanceScopeId))
            return;

        var o = tuning.TelegramDiceDailyLimit;
        if (o.GetMaxRollsPerUserPerDay(gameId) <= 0) return;

        var today = TodayInOffset(o.TimezoneOffsetHours);

        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        var row = await conn.QuerySingleOrDefaultAsync<DiceRollRow?>(
                new CommandDefinition(
                    """
                    SELECT rolls_on AS RollsOn,
                           roll_count AS RollCount
                    FROM telegram_dice_daily_rolls
                    WHERE telegram_user_id = @userId
                      AND balance_scope_id = @balanceScopeId
                      AND game_id = @gameId
                    FOR UPDATE
                    """,
                    new { userId, balanceScopeId, gameId },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

        if (row is null || row.RollsOn != today || row.RollCount <= 0)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return;
        }

        var newCount = row.RollCount - 1;
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE telegram_dice_daily_rolls SET
                    roll_count = @newCount,
                    rolls_on = CASE WHEN @newCount = 0 THEN NULL ELSE rolls_on END,
                    updated_at = now()
                WHERE telegram_user_id = @userId
                  AND balance_scope_id = @balanceScopeId
                  AND game_id = @gameId
                """,
                new { userId, balanceScopeId, gameId, newCount },
                transaction: tx,
                cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static DateOnly TodayInOffset(int hoursEastOfUtc)
    {
        var shifted = DateTimeOffset.UtcNow.AddHours(hoursEastOfUtc);
        return DateOnly.FromDateTime(shifted.DateTime);
    }

    private bool IsPrivateAdminScope(long userId, long balanceScopeId) =>
        userId == balanceScopeId && botOptions.Value.Admins.Contains(userId);

    private sealed class DiceRollRow
    {
        public DateOnly? RollsOn { get; init; }
        public int RollCount { get; init; }
    }
}
