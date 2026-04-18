using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Economics;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services.Dice;

public sealed class DiceService(
    AppDbContext db,
    IOptions<BotOptions> options,
    ClickHouseReporter reporter,
    EconomicsService economics)
{
    private readonly BotOptions _opts = options.Value;

    public async Task<DicePlayResult> PlayAsync(
        long userId, string displayName, int diceValue, long chatId,
        bool isForwarded, bool isPrivateChat, CancellationToken ct)
    {
        if (isForwarded)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "dice",
                Payload = new { type = "forwarded", user_id = userId, chat_id = chatId, dice_value = diceValue }
            });
            return new DicePlayResult(DiceOutcome.Forwarded);
        }

        var rolls = DecodeRolls(diceValue);
        var user = await EnsureUserAsync(userId, displayName, ct);

        var currentDayMs = TimeHelper.GetCurrentDayMillis();
        var isCurrentDay = currentDayMs == user.LastDayUtc;

        var totalAttempts = _opts.AttemptsLimit + user.ExtraAttempts;
        if (user.AttemptCount >= totalAttempts && isCurrentDay)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "dice",
                Payload = new { type = "attempts_limit_reached", chat_id = chatId, user_id = userId, dice_value = diceValue }
            });
            return new DicePlayResult(DiceOutcome.AttemptsLimit, TotalAttempts: totalAttempts);
        }

        var isExtraAttempt = isCurrentDay && user.AttemptCount >= _opts.AttemptsLimit;
        var gas = TaxService.GetGasTax(_opts.DiceCost);
        var loss = isExtraAttempt ? 30 : _opts.DiceCost + gas;

        if (user.Coins < loss)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "dice",
                Payload = new { type = "not_enough_coins", chat_id = chatId, user_id = userId, dice_value = diceValue, fixed_loss = loss }
            });
            return new DicePlayResult(DiceOutcome.NotEnoughCoins, Loss: loss);
        }

        var (maxFrequent, maxFrequency, _) = GetMaxFrequency(rolls);
        var prize = GetPrize(maxFrequent, maxFrequency, rolls, isExtraAttempt);
        var isWin = prize - loss > 0;

        var daysWithoutRolls = TimeHelper.GetDaysBetween(TimeHelper.GetCurrentDay(), TimeHelper.GetDateFromMillis(user.LastDayUtc));
        var tax = ComputeBankTax(user.Coins, daysWithoutRolls);
        var attemptsCount = isCurrentDay ? user.AttemptCount + 1 : 1;

        await economics.AdjustAsync(user, prize - loss, "dice.play", ct);
        user.LastDayUtc = currentDayMs;
        user.AttemptCount = attemptsCount;
        user.ExtraAttempts = isCurrentDay ? user.ExtraAttempts : 0;

        FreespinCode? issued = null;
        if (!isPrivateChat && Random.Shared.NextDouble() <= _opts.FreecodeProbability)
        {
            issued = new FreespinCode
            {
                Code = Guid.NewGuid(),
                Active = true,
                IssuedBy = userId,
                IssuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ChatId = chatId,
            };
            db.FreespinCodes.Add(issued);
        }

        await db.SaveChangesAsync(ct);

        var moreRolls = GetMoreRollsAvailable(user, _opts.AttemptsLimit);

        if (issued != null)
        {
            reporter.SendEvent(new EventData
            {
                EventType = "codegen",
                Payload = new { type = "freespin", chat_id = chatId, user_id = userId, code_text = issued.Code.ToString(), issued_at = issued.IssuedAt }
            });
        }

        reporter.SendEvent(new EventData
        {
            EventType = "dice",
            Payload = new
            {
                type = "success", chat_id = chatId, user_id = userId, dice_value = diceValue,
                is_win = isWin, prize, fixed_loss = loss, attempts_left = moreRolls,
                is_extra_attempt = isExtraAttempt, attemptsCount, tax
            }
        });

        return new DicePlayResult(
            DiceOutcome.Played, prize, loss, user.Coins, totalAttempts, moreRolls,
            tax, daysWithoutRolls, issued?.Code);
    }

    public async Task AttachFreespinMessageAsync(Guid codeGuid, int messageId, CancellationToken ct)
    {
        var code = await db.FreespinCodes.FindAsync([codeGuid], ct);
        if (code == null) return;
        code.MessageId = messageId;
        await db.SaveChangesAsync(ct);
    }

    private static (int maxFrequent, int maxFrequency, int[] rolls) GetMaxFrequency(int[] arr)
    {
        var map = new Dictionary<int, int>();
        foreach (var item in arr)
            map[item] = map.GetValueOrDefault(item) + 1;

        var maxVal = map.Values.Max();
        var maxKey = map.First(kv => kv.Value == maxVal).Key;
        return (maxKey, maxVal, arr);
    }

    private static int GetRollsSum(int[] rolls) =>
        rolls.Sum(v => v < BotOptions.StakePrice.Length ? BotOptions.StakePrice[v] : 0);

    private static int GetPrize(int maxFrequent, int maxFrequency, int[] rolls, bool isRedeem)
    {
        var sticker = maxFrequent < BotOptions.Stickers.Length ? BotOptions.Stickers[maxFrequent] : "";
        var rollsSum = GetRollsSum(rolls);

        if (isRedeem)
        {
            return (sticker, maxFrequency) switch
            {
                ("seven", 3) => 150,
                ("lemon", 3) => 70,
                ("cherry", 3) => 50,
                ("bar", 3) => 45,
                ("seven", 2) => 30 + rollsSum * 4,
                ("lemon", 2) => 20 + rollsSum * 4,
                (_, 2) => 15 + rollsSum * 4,
                _ => rollsSum,
            };
        }

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

    public static int GetMoreRollsAvailable(UserState user, int attemptsLimit)
    {
        var isCurrentDay = TimeHelper.GetCurrentDayMillis() == user.LastDayUtc;
        var total = attemptsLimit + user.ExtraAttempts;
        return Math.Max(0, isCurrentDay ? total - user.AttemptCount : attemptsLimit);
    }

    private static int[] DecodeRolls(int value) =>
    [
        ((value - 1) >> 0) & 0b11,
        ((value - 1) >> 2) & 0b11,
        ((value - 1) >> 4) & 0b11,
    ];

    private static int ComputeBankTax(int coins, int daysWithoutRolls)
    {
        if (daysWithoutRolls <= 0) return 0;
        var tax = 0;
        var balance = (double)coins;
        for (var i = 0; i < daysWithoutRolls; i++)
        {
            var mod = 1 + (daysWithoutRolls - i) * 0.04;
            var curr = TaxService.GetBankTax(balance * mod);
            tax += curr;
            balance -= curr;
        }
        return tax;
    }

    private async Task<UserState> EnsureUserAsync(long userId, string displayName, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user != null) return user;

        user = new UserState
        {
            TelegramUserId = userId,
            DisplayName = displayName,
            Coins = 100,
            LastDayUtc = TimeHelper.GetCurrentDayMillis(),
            AttemptCount = 0,
            ExtraAttempts = 0,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
