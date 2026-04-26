using Dapper;

namespace BotFramework.Host.Services;

internal sealed class TelegramDiceDailyRollLimiter(
    INpgsqlConnectionFactory connections,
    IRuntimeTuningAccessor tuning) : ITelegramDiceDailyRollLimiter
{
    public async Task<TelegramDiceRollGateResult> TryConsumeRollAsync(
        long userId, long balanceScopeId, CancellationToken ct)
    {
        var o = tuning.TelegramDiceDailyLimit;
        if (o.MaxRollsPerUserPerDay <= 0)
            return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, 0);

        var today = TodayInOffset(o.TimezoneOffsetHours);
        var max = o.MaxRollsPerUserPerDay;

        while (true)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            var row = await conn.QuerySingleOrDefaultAsync<DiceRollRow?>(
                new CommandDefinition(
                    """
                    SELECT telegram_dice_rolls_on AS RollsOn,
                           telegram_dice_roll_count AS RollCount,
                           version AS Version
                    FROM users
                    WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
                    FOR UPDATE
                    """,
                    new { userId, balanceScopeId },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

            if (row is null)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, 0, max);
            }

            var count = row.RollsOn == today ? row.RollCount : 0;

            if (count >= max)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.LimitExceeded, count, max);
            }

            var newCount = count + 1;
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE users SET
                        telegram_dice_rolls_on = @today,
                        telegram_dice_roll_count = @newCount,
                        version = version + 1,
                        updated_at = now()
                    WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId AND version = @ver
                    """,
                    new { userId, balanceScopeId, today = today.ToDateTime(TimeOnly.MinValue), newCount, ver = row.Version },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

            if (affected == 1)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return new TelegramDiceRollGateResult(TelegramDiceRollGateStatus.Allowed, newCount, max);
            }

            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task TryRefundRollAsync(long userId, long balanceScopeId, CancellationToken ct)
    {
        var o = tuning.TelegramDiceDailyLimit;
        if (o.MaxRollsPerUserPerDay <= 0) return;

        while (true)
        {
            await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

            var row = await conn.QuerySingleOrDefaultAsync<DiceRollRow?>(
                new CommandDefinition(
                    """
                    SELECT telegram_dice_roll_count AS RollCount,
                           version AS Version
                    FROM users
                    WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId
                    FOR UPDATE
                    """,
                    new { userId, balanceScopeId },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

            if (row is null || row.RollCount <= 0)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            var newCount = row.RollCount - 1;
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE users SET
                        telegram_dice_roll_count = @newCount,
                        telegram_dice_rolls_on = CASE WHEN @newCount = 0 THEN NULL ELSE telegram_dice_rolls_on END,
                        version = version + 1,
                        updated_at = now()
                    WHERE telegram_user_id = @userId AND balance_scope_id = @balanceScopeId AND version = @ver
                    """,
                    new { userId, balanceScopeId, newCount, ver = row.Version },
                    transaction: tx,
                    cancellationToken: ct)).ConfigureAwait(false);

            if (affected == 1)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            await tx.RollbackAsync(ct).ConfigureAwait(false);
        }
    }

    private static DateOnly TodayInOffset(int hoursEastOfUtc)
    {
        var shifted = DateTimeOffset.UtcNow.AddHours(hoursEastOfUtc);
        return DateOnly.FromDateTime(shifted.DateTime);
    }

    private sealed class DiceRollRow
    {
        public DateOnly? RollsOn { get; init; }
        public int RollCount { get; init; }
        public long Version { get; init; }
    }
}
