// ─────────────────────────────────────────────────────────────────────────────
// DiceService — application service for the 🎰 slots roll.
//
// Ported from src/CasinoShiz.Core/Services/Dice/DiceService.cs, minus the
// per-user attempts counter and bank-tax windowing. The core gameplay — decode
// Telegram's encoded dice value, pick a sticker triple, compute prize from the
// published payout table — ships verbatim.
//
// Stateless by design: no aggregate, no repository. Each call is debit →
// resolve → credit → audit → analytics → publish, all in the same request
// scope. EconomicsService makes each mutation atomic on its own row; we
// deliberately accept that a failure between debit and credit leaves the
// bettor short by the stake. Matches the live bot's behavior today (same
// non-transactional boundary) and is cheap to revisit by introducing a
// transactional wrapper on IEconomicsService later.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Dice;

public interface IDiceService
{
    Task<DicePlayResult> PlayAsync(
        long userId,
        string displayName,
        int diceValue,
        long chatId,
        bool isForwarded,
        CancellationToken ct);
}

public sealed class DiceService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDiceHistoryStore history,
    IDomainEventBus events,
    ITelegramDiceDailyRollLimiter telegramDiceRolls,
    IRuntimeTuningAccessor tuning) : IDiceService
{
    private static readonly string[] Stickers = ["bar", "cherry", "lemon", "seven"];
    private static readonly int[] StakePrice = [1, 1, 2, 3];

    public async Task<DicePlayResult> PlayAsync(
        long userId,
        string displayName,
        int diceValue,
        long chatId,
        bool isForwarded,
        CancellationToken ct)
    {
        if (isForwarded)
        {
            analytics.Track("dice", "forwarded", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["chat_id"] = chatId,
                ["dice_value"] = diceValue,
            });
            return new DicePlayResult(DiceOutcome.Forwarded);
        }

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, MiniGameIds.Dice, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new DicePlayResult(
                DiceOutcome.DailyRollLimitExceeded,
                DailyDiceUsed: gate.UsedToday,
                DailyDiceLimit: gate.Limit);

        var diceOpts = tuning.GetSection<DiceOptions>(DiceOptions.SectionName);
        var gas = TaxService.GetGasTax(diceOpts.Cost);
        var loss = diceOpts.Cost + gas;

        if (!await economics.TryDebitAsync(userId, chatId, loss, reason: "dice.stake", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, MiniGameIds.Dice, ct);
            analytics.Track("dice", "not_enough_coins", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["chat_id"] = chatId,
                ["dice_value"] = diceValue,
                ["fixed_loss"] = loss,
            });
            return new DicePlayResult(DiceOutcome.NotEnoughCoins, Loss: loss);
        }

        var rolls = DecodeRolls(diceValue);
        var (maxFrequent, maxFrequency) = GetMaxFrequency(rolls);
        var prize = GetPrize(maxFrequent, maxFrequency, rolls);

        if (prize > 0)
            await economics.CreditAsync(userId, chatId, prize, reason: "dice.prize", ct);

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);

        var rolledAt = DateTimeOffset.UtcNow;
        await history.AppendAsync(new DiceRoll(
            Id: Guid.NewGuid(),
            UserId: userId,
            DiceValue: diceValue,
            Prize: prize,
            Loss: loss,
            RolledAt: rolledAt), ct);

        analytics.Track("dice", "success", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["dice_value"] = diceValue,
            ["prize"] = prize,
            ["fixed_loss"] = loss,
            ["is_win"] = prize - loss > 0,
        });

        await events.PublishAsync(
            new DiceRollCompleted(
                UserId: userId,
                DiceValue: diceValue,
                Prize: prize,
                Loss: loss,
                OccurredAt: rolledAt.ToUnixTimeMilliseconds()),
            ct);

        await TelegramMiniGameRedeemDrops.MaybePublishAsync(
            events,
            diceOpts.RedeemDropChance,
            userId,
            chatId,
            MiniGameIds.Dice,
            rolledAt.ToUnixTimeMilliseconds(),
            ct);

        return new DicePlayResult(
            DiceOutcome.Played,
            prize,
            loss,
            balance,
            gas,
            gate.UsedToday,
            gate.Limit);
    }

    private static (int maxFrequent, int maxFrequency) GetMaxFrequency(int[] arr)
    {
        var map = new Dictionary<int, int>();
        foreach (var item in arr)
            map[item] = map.GetValueOrDefault(item) + 1;

        var maxVal = map.Values.Max();
        var maxKey = map.First(kv => kv.Value == maxVal).Key;
        return (maxKey, maxVal);
    }

    private static int GetRollsSum(int[] rolls) =>
        rolls.Sum(v => v < StakePrice.Length ? StakePrice[v] : 0);

    private static int GetPrize(int maxFrequent, int maxFrequency, int[] rolls)
    {
        var sticker = maxFrequent < Stickers.Length ? Stickers[maxFrequent] : "";
        var rollsSum = GetRollsSum(rolls);

        return (sticker, maxFrequency) switch
        {
            ("seven", 3) => 77,
            ("lemon", 3) => 30,
            ("cherry", 3) => 23,
            ("bar", 3) => 21,
            ("seven", 2) => 10 + rollsSum,
            ("lemon", 2) => 6 + rollsSum,
            (_, 2) => 4 + rollsSum,
            _ => rollsSum - 3,
        };
    }

    // Telegram encodes the slot-machine dice value as a packed base-4 triple:
    // each 2-bit group is one reel's sticker index (0..3). Decode back to an
    // int[3] so the sticker lookup can be shared with the prize table.
    private static int[] DecodeRolls(int value) =>
    [
        ((value - 1) >> 0) & 0b11,
        ((value - 1) >> 2) & 0b11,
        ((value - 1) >> 4) & 0b11,
    ];
}
