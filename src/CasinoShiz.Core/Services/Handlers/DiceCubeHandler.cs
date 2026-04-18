using CasinoShiz.Services.Dice;
using CasinoShiz.Services.Pipeline;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace CasinoShiz.Services.Handlers;

[Command("/dice")]
[MessageDice("🎲")]
public sealed class DiceCubeHandler(DiceCubeService service) : IUpdateHandler
{
    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        var msg = update.Message!;
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        if (msg.Dice?.Emoji == "🎲")
        {
            await HandleRollAsync(bot, msg, userId, displayName, chatId, reply, ct);
            return;
        }

        var parts = (msg.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var action = parts.Length > 1 ? parts[1] : "";

        switch (action)
        {
            case "bet": await HandleBetAsync(bot, userId, displayName, chatId, parts, reply, ct); break;
            default:
                await bot.SendMessage(chatId,
                    "Используй: <code>/dice bet &lt;сумма&gt;</code>, затем брось кубик 🎲",
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                break;
        }
    }

    private async Task HandleBetAsync(ITelegramBotClient bot, long userId, string displayName, long chatId,
        string[] parts, ReplyParameters reply, CancellationToken ct)
    {
        if (parts.Length < 3 || !int.TryParse(parts[2], out var amount))
        {
            await bot.SendMessage(chatId, "Укажи ставку: <code>/dice bet 50</code>",
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
            return;
        }

        var r = await service.PlaceBetAsync(userId, displayName, chatId, amount, ct);
        var text = r.Error switch
        {
            CubeBetError.None => $"Ставка {r.Amount} принята. Теперь кинь кубик 🎲\nВыплаты: 4→x2, 5→x3, 6→x5",
            CubeBetError.InvalidAmount => "Неверная сумма ставки",
            CubeBetError.NotEnoughCoins => $"Недостаточно монет (баланс: {r.Balance})",
            CubeBetError.AlreadyPending => $"У тебя уже есть ставка {r.PendingAmount} в этом чате — брось кубик 🎲",
            _ => "Не удалось принять ставку",
        };
        await bot.SendMessage(chatId, text, replyParameters: reply, cancellationToken: ct);
    }

    private async Task HandleRollAsync(ITelegramBotClient bot, Message msg, long userId, string displayName,
        long chatId, ReplyParameters reply, CancellationToken ct)
    {
        var face = msg.Dice!.Value;
        var r = await service.RollAsync(userId, displayName, chatId, face, ct);

        if (r.Outcome == CubeRollOutcome.NoBet)
        {
            await bot.SendMessage(chatId, "Сначала сделай ставку: <code>/dice bet &lt;сумма&gt;</code>",
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
            return;
        }

        var text = r.Payout > 0
            ? $"Выпало <b>{r.Face}</b> — x{r.Multiplier}! Ты забираешь <b>{r.Payout}</b> монет. Баланс: {r.Balance}"
            : $"Выпало <b>{r.Face}</b> — увы, твоя ставка {r.Bet} сгорела. Баланс: {r.Balance}";
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
    }
}
