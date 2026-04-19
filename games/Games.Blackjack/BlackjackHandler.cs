using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Blackjack;

[Command("/blackjack")]
[CallbackPrefix("bj:")]
public sealed partial class BlackjackHandler(
    IBlackjackService service,
    ILocalizer localizer,
    IOptions<BlackjackOptions> options,
    ILogger<BlackjackHandler> logger) : IUpdateHandler
{
    private readonly BlackjackOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery != null)
        {
            await DispatchCallbackAsync(ctx, ctx.Update.CallbackQuery);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        var parts = msg.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var bet))
        {
            var (existing, existingMsgId) = await service.GetSnapshotAsync(userId, ctx.Ct);
            if (existing != null)
            {
                await SendOrEditStateAsync(ctx, userId, chatId,
                    new BlackjackResult(BlackjackError.None, existing, existingMsgId));
                return;
            }
            await ctx.Bot.SendMessage(
                chatId,
                string.Format(Loc("usage"), _opts.MinBet, _opts.MaxBet),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var result = await service.StartAsync(userId, displayName, chatId, bet, ctx.Ct);
        if (result.Error != BlackjackError.None)
        {
            await ctx.Bot.SendMessage(chatId, ErrorText(result.Error),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        await SendOrEditStateAsync(ctx, userId, chatId, result);
    }

    private async Task DispatchCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        try { await ctx.Bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ctx.Ct); } catch { /* best-effort */ }

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;
        var action = cbq.Data?.Split(':').ElementAtOrDefault(1);

        BlackjackResult result = action switch
        {
            "hit" => await service.HitAsync(userId, ctx.Ct),
            "stand" => await service.StandAsync(userId, ctx.Ct),
            "double" => await service.DoubleAsync(userId, ctx.Ct),
            _ => new BlackjackResult(BlackjackError.NoActiveHand, null),
        };

        if (result.Error != BlackjackError.None)
        {
            await ctx.Bot.SendMessage(chatId, ErrorText(result.Error),
                parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
            return;
        }

        await SendOrEditStateAsync(ctx, userId, chatId, result);
    }

    private async Task SendOrEditStateAsync(UpdateContext ctx, long userId, long chatId, BlackjackResult result)
    {
        var snap = result.Snapshot!;
        var text = BlackjackRenderer.Render(snap, localizer);
        var markup = BlackjackRenderer.BuildKeyboard(snap, localizer);
        var stateMessageId = result.StateMessageId;

        if (stateMessageId.HasValue)
        {
            try
            {
                await ctx.Bot.EditMessageText(chatId, stateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch { /* fall through */ }
        }

        try
        {
            var sent = await ctx.Bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
            if (!snap.Outcome.HasValue)
                await service.SetStateMessageIdAsync(userId, sent.MessageId, ctx.Ct);
        }
        catch (Exception ex)
        {
            LogStateSendFailed(userId, ex);
        }
    }

    private string Loc(string key) => localizer.Get("blackjack", key);

    private string ErrorText(BlackjackError err) => err switch
    {
        BlackjackError.InvalidBet => string.Format(Loc("err.invalid_bet"), _opts.MinBet, _opts.MaxBet),
        BlackjackError.NotEnoughCoins => Loc("err.not_enough_coins"),
        BlackjackError.HandInProgress => Loc("err.hand_in_progress"),
        BlackjackError.NoActiveHand => Loc("err.no_active_hand"),
        BlackjackError.CannotDouble => Loc("err.cannot_double"),
        _ => Loc("err.generic"),
    };

    [LoggerMessage(EventId = 2301, Level = LogLevel.Debug, Message = "blackjack.state.send_failed user={UserId}")]
    partial void LogStateSendFailed(long userId, Exception exception);
}
