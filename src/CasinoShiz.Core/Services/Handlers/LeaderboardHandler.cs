using CasinoShiz.Configuration;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Dice;
using CasinoShiz.Services.Leaderboard;
using CasinoShiz.Services.Pipeline;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static CasinoShiz.Helpers.RussianPlural;

namespace CasinoShiz.Services.Handlers;

[Command("/top")]
[Command("/balance")]
[Command("/help")]
[Command("/__debug")]
public sealed class LeaderboardHandler(
    LeaderboardService service,
    IOptions<BotOptions> options) : IUpdateHandler
{
    private readonly BotOptions _opts = options.Value;

    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var text = msg.Text!;

        if (text.StartsWith("/__debug"))
            await HandleDebug(bot, msg, ct);
        else if (text.StartsWith("/help"))
            await HandleHelp(bot, msg, ct);
        else if (text.StartsWith("/top"))
            await HandleTop(bot, msg, ct);
        else if (text.StartsWith("/balance"))
            await HandleBalance(bot, msg, ct);
    }

    private static Task HandleDebug(ITelegramBotClient bot, Message msg, CancellationToken ct) =>
        bot.SendMessage(msg.Chat.Id, $"userId : {msg.From?.Id}\nchatId : {msg.Chat.Id}",
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);

    private static Task HandleHelp(ITelegramBotClient bot, Message msg, CancellationToken ct) =>
        bot.SendMessage(msg.Chat.Id, Locales.Help(), parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);

    private async Task HandleTop(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var limit = parts.Length > 1 && parts[1] == "full" ? 0 : 15;

        var board = await service.GetTopAsync(msg.Chat.Id, limit, ct);

        if (board.Places.Count == 0)
        {
            await bot.SendMessage(msg.Chat.Id,
                "Опа, в топе никого! Похоже никто не крутил последнее время!",
                cancellationToken: ct);
            return;
        }

        var placeStrings = board.Places.Select((entry, i) =>
        {
            var isFirst = i == 0;
            if (entry.Users.Count == 1)
                return $"{entry.Place}. {FormatUser(entry.Users[0], isFirst)}";
            return $"{entry.Place}.\n  - {string.Join("\n  - ", entry.Users.Select(u => FormatUser(u, isFirst)))}";
        });

        var lines = new List<string> { Locales.TopPlayers() };
        lines.AddRange(placeStrings);
        if (limit != 0) lines.Add(Locales.TopPlayersFull());
        lines.Add(Locales.HiddenReminder());

        await bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
    }

    private async Task HandleBalance(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var bal = await service.GetBalanceAsync(userId, displayName, ct);

        var text = bal.Visible
            ? Locales.YourBalance(bal.Coins)
            : $"{Locales.YourBalance(bal.Coins)}\n{Locales.YourBalanceHidden(_opts.DaysOfInactivityToHideInTop)}";

        await bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
    }

    private string FormatUser(LeaderboardUser user, bool isFirstPlace)
    {
        var proxy = new UserState
        {
            TelegramUserId = user.TelegramUserId,
            DisplayName = user.DisplayName,
            Coins = user.Coins,
            LastDayUtc = user.LastDayUtc,
            AttemptCount = user.AttemptCount,
            ExtraAttempts = user.ExtraAttempts,
        };
        var moreRolls = DiceService.GetMoreRollsAvailable(proxy, _opts.AttemptsLimit);
        var name = NameDecorators.DecorateName(user.DisplayName, user.Coins, isFirstPlace ? "👑" : null);
        var rollsText = moreRolls > 0
            ? $" (ещё {Plural(moreRolls, ["попытка", "попытки", "попыток"], true)})"
            : "";
        return $"{name} - {user.Coins}{rollsText}";
    }
}
