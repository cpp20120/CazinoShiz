using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using Microsoft.EntityFrameworkCore;

namespace CasinoShiz.Services.Dice;

public sealed partial class DiceCubeService(
    AppDbContext db,
    ClickHouseReporter reporter,
    EconomicsService economics,
    ILogger<DiceCubeService> logger)
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0,
        [2] = 0,
        [3] = 0,
        [4] = 2,
        [5] = 3,
        [6] = 5,
    };

    public async Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0) return CubeBetResult.Fail(CubeBetError.InvalidAmount);

        var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
        if (amount > user.Coins) return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, user.Coins);

        var existing = await db.DiceCubeBets.FindAsync([userId, chatId], ct);
        if (existing != null) return CubeBetResult.Fail(CubeBetError.AlreadyPending, user.Coins, existing.Amount);

        await economics.DebitAsync(user, amount, "dicecube.bet", ct);
        db.DiceCubeBets.Add(new DiceCubeBet
        {
            UserId = userId,
            ChatId = chatId,
            Amount = amount,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        await db.SaveChangesAsync(ct);

        LogCubeBetPlaced(userId, chatId, amount);
        reporter.SendEvent(new EventData
        {
            EventType = "dicecube_bet",
            Payload = new { user_id = userId, chat_id = chatId, amount }
        });

        return new CubeBetResult(CubeBetError.None, amount, user.Coins);
    }

    public async Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await db.DiceCubeBets.FindAsync([userId, chatId], ct);
        if (bet == null) return new CubeRollResult(CubeRollOutcome.NoBet);

        var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(user, payout, "dicecube.payout", ct);

        db.DiceCubeBets.Remove(bet);
        await db.SaveChangesAsync(ct);

        LogCubeRoll(userId, chatId, face, bet.Amount, payout);
        reporter.SendEvent(new EventData
        {
            EventType = "dicecube_roll",
            Payload = new { user_id = userId, chat_id = chatId, face, bet = bet.Amount, multiplier, payout }
        });

        return new CubeRollResult(CubeRollOutcome.Rolled, face, bet.Amount, multiplier, payout, user.Coins);
    }

    [LoggerMessage(LogLevel.Information, "dicecube.bet.placed user={UserId} chat={ChatId} amount={Amount}")]
    partial void LogCubeBetPlaced(long userId, long chatId, int amount);

    [LoggerMessage(LogLevel.Information, "dicecube.roll user={UserId} chat={ChatId} face={Face} bet={Bet} payout={Payout}")]
    partial void LogCubeRoll(long userId, long chatId, int face, int bet, int payout);
}
