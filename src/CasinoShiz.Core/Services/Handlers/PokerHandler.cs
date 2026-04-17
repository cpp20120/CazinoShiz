using CasinoShiz.Configuration;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Pipeline;
using CasinoShiz.Services.Poker.Application;
using CasinoShiz.Services.Poker.Domain;
using CasinoShiz.Services.Poker.Presentation;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.Handlers;

[Command("/poker")]
[CallbackPrefix("poker:")]
public sealed partial class PokerHandler(
    PokerService service,
    IOptions<BotOptions> options,
    ILogger<PokerHandler> logger) : IUpdateHandler
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

        if (msg.Chat.Type != ChatType.Private)
        {
            await bot.SendMessage(msg.Chat.Id, Locales.PokerOnlyPrivate(),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            return;
        }

        var command = PokerCommandParser.ParseText(msg.Text);
        await DispatchTextAsync(bot, msg, command, ct);
    }

    private async Task DispatchTextAsync(ITelegramBotClient bot, Message msg, PokerCommand command, CancellationToken ct)
    {
        var userId = msg.From!.Id;
        var chatId = msg.Chat.Id;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (command)
        {
            case PokerCommand.Create:
                await ExecuteCreate(bot, userId, displayName, chatId, ct);
                break;
            case PokerCommand.Join j:
                await ExecuteJoin(bot, userId, displayName, chatId, j.Code, ct);
                break;
            case PokerCommand.JoinMissingCode:
                await bot.SendMessage(chatId, "Укажи код: /poker join КОД", cancellationToken: ct);
                break;
            case PokerCommand.Start:
                await ExecuteStart(bot, userId, chatId, ct);
                break;
            case PokerCommand.Leave:
                await ExecuteLeave(bot, userId, chatId, ct);
                break;
            case PokerCommand.Status:
                await ExecuteStatus(bot, userId, chatId, ct);
                break;
            case PokerCommand.Raise r:
                await ApplyAction(bot, userId, chatId, "raise", r.Amount, ct);
                break;
            case PokerCommand.RaiseMissingAmount:
                await bot.SendMessage(chatId, "Укажи сумму: /poker raise СУММА", cancellationToken: ct);
                break;
            default:
                await bot.SendMessage(chatId, Locales.PokerUsage(),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                break;
        }
    }

    private async Task DispatchCallbackAsync(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        try { await bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct); } catch (Exception) { /* best-effort ack */ }

        var command = PokerCommandParser.ParseCallback(cbq.Data);
        if (command == null) return;

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;

        switch (command)
        {
            case PokerCommand.PlayerAction pa:
                await ApplyAction(bot, userId, chatId, pa.Action, pa.Amount, ct);
                break;
            case PokerCommand.RaiseMenu:
                await ShowRaiseMenu(bot, userId, chatId, ct);
                break;
        }
    }


    private async Task ExecuteCreate(ITelegramBotClient bot, long userId, string displayName, long chatId, CancellationToken ct)
    {
        var r = await service.CreateTableAsync(userId, displayName, chatId, ct);
        if (r.Error != PokerError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        await bot.SendMessage(chatId, Locales.PokerTableCreated(r.InviteCode, r.BuyIn),
            parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task ExecuteJoin(ITelegramBotClient bot, long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        var r = await service.JoinTableAsync(userId, displayName, chatId, code, ct);
        if (r.Error != PokerError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        await bot.SendMessage(chatId, Locales.PokerJoined(code.ToUpperInvariant(), r.Seated, r.Max),
            parseMode: ParseMode.Html, cancellationToken: ct);
        if (r.Snapshot != null) await BroadcastAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteStart(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var r = await service.StartHandAsync(userId, ct);
        if (r.Error != PokerError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot != null) await BroadcastAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteLeave(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var r = await service.LeaveTableAsync(userId, ct);
        if (r.Error != PokerError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        await bot.SendMessage(chatId, Locales.PokerLeft(), cancellationToken: ct);
        if (r.Snapshot != null && !r.TableClosed) await BroadcastAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteStatus(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var (snap, mySeat) = await service.FindMyTableAsync(userId, ct);
        if (snap == null || mySeat == null) { await SendError(bot, chatId, PokerError.NoTable, ct); return; }
        await SendOrEditStateAsync(bot, snap, mySeat, ct);
    }

    private async Task ApplyAction(ITelegramBotClient bot, long userId, long chatId, string verb, int amount, CancellationToken ct)
    {
        var r = await service.ApplyPlayerActionAsync(userId, verb, amount, ct);
        if (r.Error != PokerError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot != null) await BroadcastAsync(bot, r.Snapshot, ct, r.Showdown);
    }

    public async Task BroadcastAutoActionAsync(ITelegramBotClient bot, ActionResult r, CancellationToken ct)
    {
        if (r.Snapshot == null) return;
        if (r.AutoActorName != null && r.AutoKind != null)
        {
            string msg = r.AutoKind == AutoAction.Fold
                ? Locales.PokerAutoFold(r.AutoActorName)
                : Locales.PokerAutoCheck(r.AutoActorName);
            foreach (var s in r.Snapshot.Seats.Where(s => s.ChatId != 0))
            {
                try { await bot.SendMessage(s.ChatId, msg, cancellationToken: ct); } catch (Exception) { /* seat may have stale chat */ }
            }
        }
        await BroadcastAsync(bot, r.Snapshot, ct, r.Showdown);
    }

    private async Task ShowRaiseMenu(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var (snap, seat) = await service.FindMyTableAsync(userId, ct);
        if (snap == null || seat == null) return;
        var table = snap.Table;

        var toCall = Math.Max(0, table.CurrentBet - seat.CurrentBet);
        var minRaise = Math.Max(table.BigBlind, table.MinRaise);
        var minTotal = table.CurrentBet + minRaise;
        var potSize = table.Pot + toCall;
        var maxTotal = seat.CurrentBet + seat.Stack;

        var options = new List<int>();
        if (minTotal <= maxTotal) options.Add(minTotal);
        var twoX = table.CurrentBet * 2;
        if (twoX >= minTotal && twoX <= maxTotal && !options.Contains(twoX)) options.Add(twoX);
        var potTotal = table.CurrentBet + potSize;
        if (potTotal >= minTotal && potTotal <= maxTotal && !options.Contains(potTotal)) options.Add(potTotal);
        if (!options.Contains(maxTotal)) options.Add(maxTotal);

        var buttons = options.Select(v => InlineKeyboardButton.WithCallbackData(
            v == maxTotal ? $"Олл-ин {v}" : $"Рейз {v}", $"poker:raise:{v}")).ToArray();
        var markup = new InlineKeyboardMarkup(buttons.Chunk(2).Select(row => row.ToArray()));

        await bot.SendMessage(chatId,
            $"Выбери сумму (мин. {minTotal}, макс. {maxTotal}). Или текстом: <code>/poker raise N</code>",
            parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
    }


    private async Task BroadcastAsync(ITelegramBotClient bot, TableSnapshot snapshot, CancellationToken ct, List<ShowdownEntry>? showdown = null)
    {
        if (showdown != null)
        {
            string text = PokerStateRenderer.RenderShowdown(snapshot.Table, snapshot.Seats,
                showdown.Select(e => (e.Seat, e.Rank, e.Won)));
            foreach (var s in snapshot.Seats.Where(s => s.ChatId != 0))
            {
                try { await bot.SendMessage(s.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ct); }
                catch (Exception ex) { LogPokerShowdownSendFailedUserU(s.UserId, ex); }
            }
        }

        foreach (var seat in snapshot.Seats.Where(s => s.ChatId != 0))
            await SendOrEditStateAsync(bot, snapshot, seat, ct);
    }

    private async Task SendOrEditStateAsync(ITelegramBotClient bot, TableSnapshot snapshot, PokerSeat viewer, CancellationToken ct)
    {
        string text = PokerStateRenderer.RenderTable(snapshot.Table, snapshot.Seats, viewer.UserId);
        InlineKeyboardMarkup? markup = BuildActionMarkup(snapshot.Table, viewer);

        if (viewer.StateMessageId.HasValue)
        {
            try
            {
                await bot.EditMessageText(viewer.ChatId, viewer.StateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch (Exception) { /* fall through to resend below */ }
        }

        try
        {
            var sent = await bot.SendMessage(viewer.ChatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
            await service.SetStateMessageIdAsync(viewer.UserId, sent.MessageId, ct);
        }
        catch (Exception ex)
        {
            LogPokerStateSendFailedUserU(viewer.UserId, ex);
        }
    }

    private static InlineKeyboardMarkup? BuildActionMarkup(PokerTable table, PokerSeat viewer)
    {
        if (table.Status != PokerTableStatus.HandActive) return null;
        if (viewer.Position != table.CurrentSeat) return null;
        if (viewer.Status != PokerSeatStatus.Seated) return null;

        int toCall = Math.Max(0, table.CurrentBet - viewer.CurrentBet);
        var row1 = new List<InlineKeyboardButton>();
        if (toCall == 0)
            row1.Add(InlineKeyboardButton.WithCallbackData("✅ Чек", "poker:check"));
        else
            row1.Add(InlineKeyboardButton.WithCallbackData($"📞 Колл {toCall}", "poker:call"));
        row1.Add(InlineKeyboardButton.WithCallbackData("❌ Фолд", "poker:fold"));

        var row2 = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("💰 Рейз", "poker:raise_menu"),
            InlineKeyboardButton.WithCallbackData("🔥 Олл-ин", "poker:allin"),
        };
        return new InlineKeyboardMarkup([row1.ToArray(), row2.ToArray()]);
    }

    private async Task SendError(ITelegramBotClient bot, long chatId, PokerError error, CancellationToken ct)
    {
        string text = error switch
        {
            PokerError.NotEnoughCoins => Locales.PokerNotEnoughCoins(_opts.PokerBuyIn),
            PokerError.AlreadySeated => Locales.PokerAlreadySeated(),
            PokerError.TableNotFound => Locales.PokerTableNotFound(),
            PokerError.TableFull => Locales.PokerTableFull(),
            PokerError.HandInProgress => Locales.PokerHandInProgress(),
            PokerError.NotHost => Locales.PokerNotHost(),
            PokerError.NeedTwo => Locales.PokerNeedTwo(),
            PokerError.NoTable => Locales.PokerNoTable(),
            PokerError.NotYourTurn => Locales.PokerNotYourTurn(),
            PokerError.CannotCheck => Locales.PokerCannotCheck(),
            PokerError.RaiseTooSmall => Locales.PokerRaiseTooSmall(0),
            PokerError.RaiseTooLarge => Locales.PokerRaiseTooLarge(0),
            _ => Locales.PokerInvalidAction(),
        };
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    [LoggerMessage(LogLevel.Debug, "poker.showdown.send_failed user={U}")]
    partial void LogPokerShowdownSendFailedUserU(long u, Exception exception);

    [LoggerMessage(LogLevel.Debug, "poker.state.send_failed user={U}")]
    partial void LogPokerStateSendFailedUserU(long u, Exception exception);
}
