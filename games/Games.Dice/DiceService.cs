// ─────────────────────────────────────────────────────────────────────────────
// DiceService — application service for the 🎰 slots roll.
//
// Two-phase design (like DiceCubeService):
//   1. PlaceBetAsync: Debit stake, store pending bet. User returns bet info.
//   2. ResolveBetAsync: Resolve dice roll, compute prize, credit payout, clean up bet.
//   3. AbortBetAfterSendDiceFailedAsync: If bot's SendDice fails, refund the debit.
//
// This prevents lost bets when the Telegram message fails to send after debit.
// Previously, the service did everything atomically in one call; if the message
// send failed in the handler's try-catch, the debit was already done but the
// user saw no result and couldn't retry. Now the bet is only consumed if BOTH
// the debit and message-send succeed.
//
// Ported from src/CasinoShiz.Core/Services/Dice/DiceService.cs, minus the
// per-user attempts counter, bank-tax windowing, and freespin-code issuance.
// Those depended on the shared UserState row and will come back when #15
// ports the remainder.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Dice;

public interface IDiceService
{
    /// <summary>Phase 1: Debit stake and store pending bet. Called before SendDice.</summary>
    Task<DicePlaceBetResult> PlaceBetAsync(
        long userId,
        string displayName,
        int diceValue,
        long chatId,
        bool isForwarded,
        CancellationToken ct);

    /// <summary>Phase 2: Resolve the dice roll and credit payout. Called after dice is rolled.</summary>
    Task<DiceRollResult> ResolveBetAsync(
        long userId,
        string displayName,
        long chatId,
        CancellationToken ct);

    /// <summary>Recover from SendDice failure: refund the debit and clear the pending bet.</summary>
    Task AbortBetAfterSendDiceFailedAsync(
        long userId,
        long chatId,
        CancellationToken ct);
}

public sealed class DiceService(
    IEconomicsService economics,
    IAnalyticsService analytics,
    IDiceBetStore bets,
    IDiceHistoryStore history,
    IDomainEventBus events,
    ITelegramDiceDailyRollLimiter telegramDiceRolls,
    IRuntimeTuningAccessor tuning) : IDiceService
{
    private static readonly string[] Stickers = ["bar", "cherry", "lemon", "seven"];
    private static readonly int[] StakePrice = [1, 1, 2, 3];

    public async Task<DicePlaceBetResult> PlaceBetAsync(
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
            return new DicePlaceBetResult(DiceOutcome.Forwarded);
        }

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);

        var gate = await telegramDiceRolls.TryConsumeRollAsync(userId, chatId, ct);
        if (gate.Status == TelegramDiceRollGateStatus.LimitExceeded)
            return new DicePlaceBetResult(
                DiceOutcome.DailyRollLimitExceeded,
                DailyDiceUsed: gate.UsedToday,
                DailyDiceLimit: gate.Limit);

        var diceOpts = tuning.GetSection<DiceOptions>(DiceOptions.SectionName);
        var gas = TaxService.GetGasTax(diceOpts.Cost);
        var loss = diceOpts.Cost + gas;

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        if (loss > balance)
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            analytics.Track("dice", "not_enough_coins", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["chat_id"] = chatId,
                ["dice_value"] = diceValue,
                ["fixed_loss"] = loss,
            });
            return new DicePlaceBetResult(DiceOutcome.NotEnoughCoins, Loss: loss);
        }

        // Debit the stake
        if (!await economics.TryDebitAsync(userId, chatId, loss, reason: "dice.stake", ct))
        {
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            analytics.Track("dice", "not_enough_coins", new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["chat_id"] = chatId,
                ["dice_value"] = diceValue,
                ["fixed_loss"] = loss,
            });
            return new DicePlaceBetResult(DiceOutcome.NotEnoughCoins, Loss: loss);
        }

        // Store the pending bet so ResolveBet can find it later
        var bet = new DiceBet(userId, chatId, diceValue, loss, DateTimeOffset.UtcNow);
        if (!await bets.InsertAsync(bet, ct))
        {
            // Bet already exists or store failed; refund and fail
            await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);
            await economics.CreditAsync(userId, chatId, loss, "dice.bet.refund", ct);
            return new DicePlaceBetResult(DiceOutcome.BetStoreError);
        }

        var newBalance = balance - loss;
        analytics.Track("dice", "bet_placed", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["dice_value"] = diceValue,
            ["loss"] = loss,
        });

        return new DicePlaceBetResult(DiceOutcome.BetPlaced, loss, newBalance, gas);
    }

    public async Task<DiceRollResult> ResolveBetAsync(
        long userId,
        string displayName,
        long chatId,
        CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null)
            return new DiceRollResult(DiceOutcome.NoPendingBet);

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);

        var rolls = DecodeRolls(bet.DiceValue);
        var (maxFrequent, maxFrequency) = GetMaxFrequency(rolls);
        var prize = GetPrize(maxFrequent, maxFrequency, rolls);

        if (prize > 0)
            await economics.CreditAsync(userId, chatId, prize, reason: "dice.prize", ct);

        var balance = await economics.GetBalanceAsync(userId, chatId, ct);
        var rolledAt = DateTimeOffset.UtcNow;
        
        await history.AppendAsync(new DiceRoll(
            Id: Guid.NewGuid(),
            UserId: userId,
            DiceValue: bet.DiceValue,
            Prize: prize,
            Loss: bet.Loss,
            RolledAt: rolledAt), ct);

        await bets.DeleteAsync(userId, chatId, ct);

        analytics.Track("dice", "success", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["dice_value"] = bet.DiceValue,
            ["prize"] = prize,
            ["fixed_loss"] = bet.Loss,
            ["is_win"] = prize - bet.Loss > 0,
        });

        await events.PublishAsync(
            new DiceRollCompleted(
                UserId: userId,
                DiceValue: bet.DiceValue,
                Prize: prize,
                Loss: bet.Loss,
                OccurredAt: rolledAt.ToUnixTimeMilliseconds()),
            ct);

        return new DiceRollResult(DiceOutcome.Played, prize, bet.Loss, balance);
    }

    public async Task AbortBetAfterSendDiceFailedAsync(long userId, long chatId, CancellationToken ct)
    {
        var bet = await bets.FindAsync(userId, chatId, ct);
        if (bet == null) return;

        // Refund the debited amount
        await economics.CreditAsync(userId, chatId, bet.Loss, "dice.send_dice_failed", ct);
        await bets.DeleteAsync(userId, chatId, ct);
        await telegramDiceRolls.TryRefundRollAsync(userId, chatId, ct);

        analytics.Track("dice", "bet_aborted", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["chat_id"] = chatId,
            ["loss"] = bet.Loss,
        });
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

