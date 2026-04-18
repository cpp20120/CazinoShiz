using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;

namespace CasinoShiz.Services.Dice;

public sealed partial class DartsService(
    AppDbContext db,
    ClickHouseReporter reporter,
    EconomicsService economics,
    ILogger<DartsService> logger)
{
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0,
        [2] = 0,
        [3] = 0,
        [4] = 2,
        [5] = 3,
        [6] = 6,
    };

    public async Task<DartsBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0) return DartsBetResult.Fail(DartsBetError.InvalidAmount);

        var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
        if (amount > user.Coins) return DartsBetResult.Fail(DartsBetError.NotEnoughCoins, user.Coins);

        var existing = await db.DartsBets.FindAsync([userId, chatId], ct);
        if (existing != null) return DartsBetResult.Fail(DartsBetError.AlreadyPending, user.Coins, existing.Amount);

        await economics.DebitAsync(user, amount, "darts.bet", ct);
        db.DartsBets.Add(new DartsBet
        {
            UserId = userId,
            ChatId = chatId,
            Amount = amount,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        await db.SaveChangesAsync(ct);

        LogDartsBetPlaced(userId, chatId, amount);
        reporter.SendEvent(new EventData
        {
            EventType = "darts_bet",
            Payload = new { user_id = userId, chat_id = chatId, amount }
        });

        return new DartsBetResult(DartsBetError.None, amount, user.Coins);
    }

    public async Task<DartsThrowResult> ThrowAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await db.DartsBets.FindAsync([userId, chatId], ct);
        if (bet == null) return new DartsThrowResult(DartsThrowOutcome.NoBet);

        var user = await economics.GetOrCreateUserAsync(userId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(user, payout, "darts.payout", ct);

        db.DartsBets.Remove(bet);
        await db.SaveChangesAsync(ct);

        LogDartsThrow(userId, chatId, face, bet.Amount, payout);
        reporter.SendEvent(new EventData
        {
            EventType = "darts_throw",
            Payload = new { user_id = userId, chat_id = chatId, face, bet = bet.Amount, multiplier, payout }
        });

        return new DartsThrowResult(DartsThrowOutcome.Thrown, face, bet.Amount, multiplier, payout, user.Coins);
    }

    [LoggerMessage(LogLevel.Information, "darts.bet.placed user={UserId} chat={ChatId} amount={Amount}")]
    partial void LogDartsBetPlaced(long userId, long chatId, int amount);

    [LoggerMessage(LogLevel.Information, "darts.throw user={UserId} chat={ChatId} face={Face} bet={Bet} payout={Payout}")]
    partial void LogDartsThrow(long userId, long chatId, int face, int bet, int payout);
}
