// ─────────────────────────────────────────────────────────────────────────────
// DiceCubeService — place a cube bet, resolve on 🎲 throw.
//
// Lifecycle:
//   • PlaceBetAsync: validate amount, ensure user seeded with starting coins,
//     reject double-bets in the same chat, debit via IEconomicsService, insert
//     pending bet row.
//   • RollAsync: look up pending bet, compute multiplier from the face roll,
//     credit payout if any, delete the bet, publish DiceCubeRollCompleted.
//
// Payout table verbatim from the live service:
//     4 → x2 · 5 → x3 · 6 → x5 · (1,2,3) → 0
//
// Atomicity note: the bet-insertion and the debit are not in a single
// transaction. A crash between them leaves the coin gone but no pending bet.
// Matches the monolith's behavior (same boundary). Revisit when EconomicsService
// grows a transactional API.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

namespace Games.DiceCube;

public interface IDiceCubeService
{
    Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct);
    Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct);
}

public sealed class DiceCubeService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDiceCubeBetStore bets,
    IDomainEventBus events,
    IOptions<DiceCubeOptions> options) : IDiceCubeService
{
    private readonly int _maxBet = options.Value.MaxBet;
    public static readonly IReadOnlyDictionary<int, int> Multipliers = new Dictionary<int, int>
    {
        [1] = 0, [2] = 0, [3] = 0, [4] = 2, [5] = 3, [6] = 5,
    };

    public async Task<CubeBetResult> PlaceBetAsync(long userId, string displayName, long chatId, int amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > _maxBet) return CubeBetResult.Fail(CubeBetError.InvalidAmount);

        await economics.EnsureUserAsync(userId, displayName, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);
        if (amount > balance) return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);

        var existing = await bets.FindAsync(userId, chatId, ct);
        if (existing != null) return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance, existing.Amount);

        if (!await economics.TryDebitAsync(userId, amount, "dicecube.bet", ct))
            return CubeBetResult.Fail(CubeBetError.NotEnoughCoins, balance);

        if (!await bets.InsertAsync(new DiceCubeBet(userId, chatId, amount, DateTimeOffset.UtcNow), ct))
        {
            await economics.CreditAsync(userId, amount, "dicecube.bet.refund", ct);
            return CubeBetResult.Fail(CubeBetError.AlreadyPending, balance);
        }

        analytics.Track("dicecube", "bet", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["amount"] = amount,
        });

        return new CubeBetResult(CubeBetError.None, amount, balance - amount);
    }

    public async Task<CubeRollResult> RollAsync(long userId, string displayName, long chatId, int face, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return new CubeRollResult(CubeRollOutcome.NoBet);

        await economics.EnsureUserAsync(userId, displayName, ct);
        var multiplier = Multipliers.TryGetValue(face, out var m) ? m : 0;
        var payout = bet.Amount * multiplier;

        if (payout > 0)
            await economics.CreditAsync(userId, payout, "dicecube.payout", ct);

        await bets.DeleteAsync(userId, chatId, ct);
        var balance = await economics.GetBalanceAsync(userId, ct);

        analytics.Track("dicecube", "roll", new Dictionary<string, object?>
        {
            ["user_id"] = userId, ["chat_id"] = chatId, ["face"] = face,
            ["bet"] = bet.Amount, ["multiplier"] = multiplier, ["payout"] = payout,
        });

        await events.PublishAsync(
            new DiceCubeRollCompleted(userId, chatId, face, bet.Amount, multiplier, payout,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ct);

        return new CubeRollResult(CubeRollOutcome.Rolled, face, bet.Amount, multiplier, payout, balance);
    }
}
