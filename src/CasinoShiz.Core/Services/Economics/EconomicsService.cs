using System.Data.Common;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CasinoShiz.Services.Economics;

public sealed partial class EconomicsService(AppDbContext db, ILogger<EconomicsService> logger)
{
    private sealed class BalanceRow
    {
        public int Coins { get; set; }
        public long Version { get; set; }
    }

    public async Task CreditAsync(UserState user, int amount, string reason, CancellationToken ct = default)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be non-negative");
        if (amount == 0) return;
        await ApplyAsync(user, delta: amount, allowNegative: true, ct);
        LogCredit(user.TelegramUserId, amount, user.Coins, reason);
    }

    public async Task<bool> TryDebitAsync(UserState user, int amount, string reason, CancellationToken ct = default)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be non-negative");
        if (amount == 0) return true;
        var ok = await ApplyAsync(user, delta: -amount, allowNegative: false, ct);
        if (!ok)
        {
            LogDebitRejected(user.TelegramUserId, amount, user.Coins, reason);
            return false;
        }
        LogDebit(user.TelegramUserId, amount, user.Coins, reason);
        return true;
    }

    public async Task DebitAsync(UserState user, int amount, string reason, CancellationToken ct = default)
    {
        if (!await TryDebitAsync(user, amount, reason, ct))
            throw new InsufficientFundsException(user.TelegramUserId, amount, user.Coins);
    }

    public Task AdjustAsync(UserState user, int delta, string reason, CancellationToken ct = default)
    {
        if (delta == 0) return Task.CompletedTask;
        return delta > 0
            ? CreditAsync(user, delta, reason, ct)
            : DebitAsync(user, -delta, reason, ct);
    }

    public async Task AdjustUncheckedAsync(UserState user, int delta, string reason, CancellationToken ct = default)
    {
        if (delta == 0) return;
        await ApplyAsync(user, delta, allowNegative: true, ct);
        LogAdjustUnchecked(user.TelegramUserId, delta, user.Coins, reason);
    }

    private async Task<bool> ApplyAsync(UserState user, int delta, bool allowNegative, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            var projected = user.Coins + delta;
            if (!allowNegative && projected < 0) return false;
            user.Coins = projected;
            user.Version++;
            return true;
        }

        var conn = (DbConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        var ambientTx = db.Database.CurrentTransaction;
        IDbContextTransaction? ownTx = null;
        if (ambientTx == null)
            ownTx = await db.Database.BeginTransactionAsync(ct);
        var dbTx = (ambientTx ?? ownTx)!.GetDbTransaction();

        try
        {
            var row = await conn.QuerySingleOrDefaultAsync<BalanceRow>(
                new CommandDefinition(
                    """SELECT "Coins" AS Coins, "Version" AS Version FROM "Users" WHERE "TelegramUserId" = @userId FOR UPDATE""",
                    new { userId = user.TelegramUserId }, transaction: dbTx, cancellationToken: ct));

            if (row is null)
                throw new InvalidOperationException($"User {user.TelegramUserId} not found for balance mutation");

            var newCoins = row.Coins + delta;
            if (!allowNegative && newCoins < 0)
            {
                if (ownTx != null) await ownTx.RollbackAsync(ct);
                return false;
            }
            var newVersion = row.Version + 1;

            await conn.ExecuteAsync(
                new CommandDefinition(
                    """UPDATE "Users" SET "Coins" = @newCoins, "Version" = @newVersion WHERE "TelegramUserId" = @userId""",
                    new { userId = user.TelegramUserId, newCoins, newVersion }, transaction: dbTx, cancellationToken: ct));

            if (ownTx != null) await ownTx.CommitAsync(ct);

            user.Coins = newCoins;
            user.Version = newVersion;
            return true;
        }
        catch
        {
            if (ownTx != null)
            {
                try { await ownTx.RollbackAsync(ct); } catch { }
            }
            throw;
        }
        finally
        {
            if (ownTx != null) await ownTx.DisposeAsync();
        }
    }

    [LoggerMessage(LogLevel.Information, "economics.credit user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogCredit(long userId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Information, "economics.debit user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebit(long userId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Warning, "economics.debit_rejected user={UserId} amount={Amount} balance={Balance} reason={Reason}")]
    partial void LogDebitRejected(long userId, int amount, int balance, string reason);

    [LoggerMessage(LogLevel.Information, "economics.adjust_unchecked user={UserId} delta={Delta} balance={Balance} reason={Reason}")]
    partial void LogAdjustUnchecked(long userId, int delta, int balance, string reason);
}
