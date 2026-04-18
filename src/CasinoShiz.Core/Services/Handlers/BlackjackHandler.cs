using CasinoShiz.Configuration;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Blackjack;
using CasinoShiz.Services.Pipeline;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.Handlers;

[Command("/blackjack")]
[CallbackPrefix("bj:")]
public sealed partial class BlackjackHandler(
    BlackjackService service,
    IOptions<BotOptions> options,
    ILogger<BlackjackHandler> logger) : IUpdateHandler
{
    private readonly BotOptions _opts = options.Value;

    public async Task HandleAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.CallbackQuery != null)
        {
            await DispatchCallbackAsync(bot, update.CallbackQuery, ct);
            return;
        }

        var msg = update.Message;
        if (msg?.Text == null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var bet))
        {
            var (existing, existingMsgId) = await service.GetSnapshotAsync(userId, ct);
            if (existing != null)
            {
                await SendOrEditStateAsync(bot, userId, chatId,
                    new BlackjackResult(BlackjackError.None, existing, existingMsgId), ct);
                return;
            }
            await bot.SendMessage(chatId, Locales.BlackjackUsage(_opts.BlackjackMinBet, _opts.BlackjackMaxBet),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
            return;
        }

        var result = await service.StartAsync(userId, displayName, chatId, bet, ct);
        if (result.Error != BlackjackError.None)
        {
            await bot.SendMessage(chatId, ErrorText(result.Error),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
            return;
        }

        await SendOrEditStateAsync(bot, userId, chatId, result, ct);
    }

    private async Task DispatchCallbackAsync(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        try { await bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct); } catch (Exception) { /* best-effort */ }

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;
        var action = cbq.Data?.Split(':').ElementAtOrDefault(1);

        BlackjackResult result = action switch
        {
            "hit" => await service.HitAsync(userId, ct),
            "stand" => await service.StandAsync(userId, ct),
            "double" => await service.DoubleAsync(userId, ct),
            _ => new BlackjackResult(BlackjackError.NoActiveHand, null),
        };

        if (result.Error != BlackjackError.None)
        {
            await bot.SendMessage(chatId, ErrorText(result.Error), parseMode: ParseMode.Html, cancellationToken: ct);
            return;
        }

        await SendOrEditStateAsync(bot, userId, chatId, result, ct);
    }

    private async Task SendOrEditStateAsync(
        ITelegramBotClient bot, long userId, long chatId, BlackjackResult result, CancellationToken ct)
    {
        var snap = result.Snapshot!;
        var text = BlackjackRenderer.Render(snap);
        var markup = BlackjackRenderer.BuildKeyboard(snap);
        var stateMessageId = result.StateMessageId;

        if (stateMessageId.HasValue)
        {
            try
            {
                await bot.EditMessageText(chatId, stateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch (Exception) { /* fall through */ }
        }

        try
        {
            var sent = await bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
            if (!snap.Outcome.HasValue)
                await service.SetStateMessageIdAsync(userId, sent.MessageId, ct);
        }
        catch (Exception ex)
        {
            LogBlackjackStateSendFailed(userId, ex);
        }
    }

    private string ErrorText(BlackjackError err) => err switch
    {
        BlackjackError.InvalidBet => Locales.BlackjackInvalidBet(_opts.BlackjackMinBet, _opts.BlackjackMaxBet),
        BlackjackError.NotEnoughCoins => Locales.BlackjackNotEnoughCoins(),
        BlackjackError.HandInProgress => Locales.BlackjackHandInProgress(),
        BlackjackError.NoActiveHand => Locales.BlackjackNoActiveHand(),
        BlackjackError.CannotDouble => Locales.BlackjackCannotDouble(),
        _ => "Ошибка.",
    };

    [LoggerMessage(LogLevel.Debug, "blackjack.state.send_failed user={U}")]
    partial void LogBlackjackStateSendFailed(long u, Exception exception);
}
