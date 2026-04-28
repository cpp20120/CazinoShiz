// ─────────────────────────────────────────────────────────────────────────────
// PokerHandler — /poker + poker:* dispatcher.
//
// Port of src/CasinoShiz.Core/Services/Handlers/PokerHandler.cs. Poker tables
// are scoped to Telegram groups; the group gets one public board message while
// hole cards are revealed only through per-user callback alerts.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using BotFramework.Sdk;
using Games.Poker.Domain;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Poker;

[Command("/poker")]
[CallbackPrefix("poker:")]
public sealed partial class PokerHandler(
    IPokerService service,
    ILocalizer localizer,
    IOptions<PokerOptions> options,
    ILogger<PokerHandler> logger) : IUpdateHandler
{
    private readonly PokerOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery != null)
        {
            await DispatchCallbackAsync(ctx, ctx.Update.CallbackQuery);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("err.only_group"),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var command = PokerCommandParser.ParseText(msg.Text);
        try
        {
            await DispatchTextAsync(ctx, msg, command);
        }
        catch (Exception ex)
        {
            LogPokerCommandFailed(msg.From?.Id ?? 0, ex);
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("err.temporary_failure"),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
        }
    }

    private async Task DispatchTextAsync(UpdateContext ctx, Message msg, PokerCommand command)
    {
        var userId = msg.From!.Id;
        var chatId = msg.Chat.Id;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (command)
        {
            case PokerCommand.Create:
                await ExecuteCreate(ctx, userId, displayName, chatId);
                break;
            case PokerCommand.Join j:
                await ExecuteJoin(ctx, userId, displayName, chatId, j.Code);
                break;
            case PokerCommand.JoinCurrent:
                await ExecuteJoin(ctx, userId, displayName, chatId, "");
                break;
            case PokerCommand.JoinMissingCode:
                await ctx.Bot.SendMessage(chatId, Loc("err.join_missing_code"), cancellationToken: ctx.Ct);
                break;
            case PokerCommand.Start:
                await ExecuteStart(ctx, userId, chatId);
                break;
            case PokerCommand.Leave:
                await ExecuteLeave(ctx, userId, chatId);
                break;
            case PokerCommand.Status:
                await ExecuteStatus(ctx, userId, chatId);
                break;
            case PokerCommand.Raise r:
                await ApplyAction(ctx, userId, chatId, "raise", r.Amount);
                break;
            case PokerCommand.RaiseMissingAmount:
                await ctx.Bot.SendMessage(chatId, Loc("err.raise_missing_amount"), cancellationToken: ctx.Ct);
                break;
            default:
                await ctx.Bot.SendMessage(chatId, Loc("usage"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task DispatchCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        var command = PokerCommandParser.ParseCallback(cbq.Data);
        if (command == null)
        {
            await AnswerCallbackAsync(ctx, cbq);
            return;
        }

        if (cbq.Message?.Chat.Type is not (ChatType.Group or ChatType.Supergroup))
        {
            await AnswerCallbackAsync(ctx, cbq, Loc("err.only_group"), true);
            return;
        }

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;
        var displayName = cbq.From.Username ?? cbq.From.FirstName ?? $"User ID: {userId}";

        switch (command)
        {
            case PokerCommand.JoinCurrent:
                try
                {
                    await ExecuteJoin(ctx, userId, displayName, chatId, "");
                    await AnswerCallbackAsync(ctx, cbq);
                }
                catch (Exception ex) { await SendCallbackFailureAsync(ctx, cbq, userId, ex); }
                break;
            case PokerCommand.Start:
                try
                {
                    await ExecuteStart(ctx, userId, chatId);
                    await AnswerCallbackAsync(ctx, cbq);
                }
                catch (Exception ex) { await SendCallbackFailureAsync(ctx, cbq, userId, ex); }
                break;
            case PokerCommand.PlayerAction pa:
                try
                {
                    await ApplyAction(ctx, userId, chatId, pa.Action, pa.Amount);
                    await AnswerCallbackAsync(ctx, cbq);
                }
                catch (Exception ex) { await SendCallbackFailureAsync(ctx, cbq, userId, ex); }
                break;
            case PokerCommand.RaiseMenu:
                try
                {
                    await ShowRaiseMenu(ctx, userId, chatId);
                    await AnswerCallbackAsync(ctx, cbq);
                }
                catch (Exception ex) { await SendCallbackFailureAsync(ctx, cbq, userId, ex); }
                break;
            case PokerCommand.ShowCards:
                try { await ShowCardsAsync(ctx, cbq, userId, chatId); }
                catch (Exception ex) { await SendCallbackFailureAsync(ctx, cbq, userId, ex); }
                break;
        }
    }

    private async Task AnswerCallbackAsync(UpdateContext ctx, CallbackQuery cbq, string? text = null, bool showAlert = false)
    {
        try
        {
            await ctx.Bot.AnswerCallbackQuery(cbq.Id, text, showAlert: showAlert, cancellationToken: ctx.Ct);
        }
        catch { /* best-effort */ }
    }

    private async Task SendCallbackFailureAsync(UpdateContext ctx, CallbackQuery cbq, long userId, Exception ex)
    {
        LogPokerCommandFailed(userId, ex);
        try
        {
            await ctx.Bot.AnswerCallbackQuery(cbq.Id, Loc("err.temporary_failure"), showAlert: true,
                cancellationToken: ctx.Ct);
        }
        catch { /* best-effort */ }
    }

    private async Task ExecuteCreate(UpdateContext ctx, long userId, string displayName, long chatId)
    {
        var r = await service.CreateTableAsync(userId, displayName, chatId, ctx.Ct);
        if (r.Error != PokerError.None) { await SendError(ctx, chatId, r.Error); return; }
        await ctx.Bot.SendMessage(chatId, string.Format(Loc("created"), r.InviteCode, r.BuyIn),
            parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        var (snap, _) = await service.FindMyTableAsync(userId, chatId, ctx.Ct);
        if (snap != null) await BroadcastAsync(ctx, snap);
    }

    private async Task ExecuteJoin(UpdateContext ctx, long userId, string displayName, long chatId, string code)
    {
        var r = await service.JoinTableAsync(userId, displayName, chatId, code, ctx.Ct);
        if (r.Error != PokerError.None) { await SendError(ctx, chatId, r.Error); return; }
        var joinedCode = r.Snapshot?.Table.InviteCode ?? code.ToUpperInvariant();
        await ctx.Bot.SendMessage(chatId, string.Format(Loc("joined"), joinedCode, r.Seated, r.Max),
            parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        if (r.Snapshot != null) await BroadcastAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteStart(UpdateContext ctx, long userId, long chatId)
    {
        var r = await service.StartHandAsync(userId, chatId, ctx.Ct);
        if (r.Error != PokerError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot != null) await BroadcastAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteLeave(UpdateContext ctx, long userId, long chatId)
    {
        var r = await service.LeaveTableAsync(userId, chatId, ctx.Ct);
        if (r.Error != PokerError.None) { await SendError(ctx, chatId, r.Error); return; }
        var leftText = r.TableClosed
            ? $"{Loc("left")}\n{Loc("table_closed")}"
            : Loc("left");
        await ctx.Bot.SendMessage(chatId, leftText, cancellationToken: ctx.Ct);
        if (r.Snapshot != null && !r.TableClosed) await BroadcastAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteStatus(UpdateContext ctx, long userId, long chatId)
    {
        var (snap, mySeat) = await service.FindMyTableAsync(userId, chatId, ctx.Ct);
        if (snap == null || mySeat == null) { await SendError(ctx, chatId, PokerError.NoTable); return; }
        await SendOrEditStateAsync(ctx, snap);
    }

    private async Task ApplyAction(UpdateContext ctx, long userId, long chatId, string verb, int amount)
    {
        var r = await service.ApplyPlayerActionAsync(userId, chatId, verb, amount, ctx.Ct);
        if (r.Error != PokerError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot != null) await BroadcastAsync(ctx, r.Snapshot, r.Showdown);
    }

    public async Task BroadcastAutoActionAsync(ITelegramBotClient bot, ActionResult r, CancellationToken ct)
    {
        if (r.Snapshot == null) return;
        if (r.AutoActorName != null && r.AutoKind != null)
        {
            string key = r.AutoKind == AutoAction.Fold ? "auto.fold" : "auto.check";
            string msg = string.Format(Loc(key), r.AutoActorName);
            try { await bot.SendMessage(r.Snapshot.Table.ChatId, msg, cancellationToken: ct); } catch { /* group may be stale */ }
        }
        await BroadcastUsingBotAsync(bot, r.Snapshot, ct, r.Showdown);
    }

    private async Task ShowRaiseMenu(UpdateContext ctx, long userId, long chatId)
    {
        var (snap, seat) = await service.FindMyTableAsync(userId, chatId, ctx.Ct);
        if (snap == null || seat == null) return;
        var table = snap.Table;

        var toCall = Math.Max(0, table.CurrentBet - seat.CurrentBet);
        var minRaise = Math.Max(table.BigBlind, table.MinRaise);
        var minTotal = table.CurrentBet + minRaise;
        var potSize = table.Pot + toCall;
        var maxTotal = seat.CurrentBet + seat.Stack;

        var optionsList = new List<int>();
        if (minTotal <= maxTotal) optionsList.Add(minTotal);
        var twoX = table.CurrentBet * 2;
        if (twoX >= minTotal && twoX <= maxTotal && !optionsList.Contains(twoX)) optionsList.Add(twoX);
        var potTotal = table.CurrentBet + potSize;
        if (potTotal >= minTotal && potTotal <= maxTotal && !optionsList.Contains(potTotal)) optionsList.Add(potTotal);
        if (!optionsList.Contains(maxTotal)) optionsList.Add(maxTotal);

        var buttons = optionsList.Select(v => InlineKeyboardButton.WithCallbackData(
            v == maxTotal ? string.Format(Loc("btn.allin_amount"), v) : string.Format(Loc("btn.raise_amount"), v),
            $"poker:raise:{v}")).ToArray();
        var markup = new InlineKeyboardMarkup(buttons.Chunk(2).Select(row => row.ToArray()));

        await ctx.Bot.SendMessage(chatId,
            string.Format(Loc("raise_menu.prompt"), minTotal, maxTotal),
            parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
    }

    private async Task ShowCardsAsync(UpdateContext ctx, CallbackQuery cbq, long userId, long chatId)
    {
        var (snap, seat) = await service.FindMyTableAsync(userId, chatId, ctx.Ct);
        if (snap == null || seat == null)
        {
            await AnswerCallbackAsync(ctx, cbq, Loc("err.no_table"), true);
            return;
        }

        var text = string.IsNullOrWhiteSpace(seat.HoleCards)
            ? Loc("cards.none")
            : string.Format(Loc("cards.yours"), PokerStateRenderer.RenderCards(seat.HoleCards));
        await AnswerCallbackAsync(ctx, cbq, text, true);
    }

    private Task BroadcastAsync(UpdateContext ctx, TableSnapshot snapshot, List<ShowdownEntry>? showdown = null) =>
        BroadcastUsingBotAsync(ctx.Bot, snapshot, ctx.Ct, showdown);

    private async Task BroadcastUsingBotAsync(ITelegramBotClient bot, TableSnapshot snapshot, CancellationToken ct, List<ShowdownEntry>? showdown = null)
    {
        if (showdown != null)
        {
            string text = PokerStateRenderer.RenderShowdown(
                snapshot.Table,
                showdown.Select(e => (e.Seat, e.Rank, e.Won, e.HoleCards)),
                localizer);
            try { await bot.SendMessage(snapshot.Table.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ct); }
            catch (Exception ex) { LogPokerShowdownSendFailed(snapshot.Table.ChatId, ex); }
        }

        await SendOrEditStateUsingBotAsync(bot, snapshot, ct);
    }

    private Task SendOrEditStateAsync(UpdateContext ctx, TableSnapshot snapshot) =>
        SendOrEditStateUsingBotAsync(ctx.Bot, snapshot, ctx.Ct);

    private async Task SendOrEditStateUsingBotAsync(ITelegramBotClient bot, TableSnapshot snapshot, CancellationToken ct)
    {
        string text = PokerStateRenderer.RenderTable(snapshot.Table, snapshot.Seats, null, localizer);
        InlineKeyboardMarkup? markup = BuildGroupMarkup(snapshot);

        if (snapshot.Table.StateMessageId.HasValue)
        {
            try
            {
                await bot.EditMessageText(snapshot.Table.ChatId, snapshot.Table.StateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch { /* fall through to resend below */ }
        }

        try
        {
            var sent = await bot.SendMessage(snapshot.Table.ChatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
            await service.SetTableStateMessageIdAsync(snapshot.Table.InviteCode, sent.MessageId, ct);
        }
        catch (Exception ex)
        {
            LogPokerStateSendFailed(snapshot.Table.ChatId, ex);
        }
    }

    private InlineKeyboardMarkup? BuildGroupMarkup(TableSnapshot snapshot)
    {
        var table = snapshot.Table;
        if (table.Status is PokerTableStatus.Seating or PokerTableStatus.HandComplete)
        {
            return new InlineKeyboardMarkup([
                [
                    InlineKeyboardButton.WithCallbackData(Loc("btn.join"), "poker:join"),
                    InlineKeyboardButton.WithCallbackData(Loc("btn.start"), "poker:start"),
                ],
            ]);
        }

        var current = snapshot.Seats.FirstOrDefault(s => s.Position == table.CurrentSeat);
        if (current == null)
        {
            return new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData(Loc("btn.cards"), "poker:cards")],
            ]);
        }

        return BuildActionMarkup(table, current);
    }

    private InlineKeyboardMarkup? BuildActionMarkup(PokerTable table, PokerSeat viewer)
    {
        if (table.Status != PokerTableStatus.HandActive) return null;
        if (viewer.Status != PokerSeatStatus.Seated) return null;

        int toCall = Math.Max(0, table.CurrentBet - viewer.CurrentBet);
        var row1 = new List<InlineKeyboardButton>();
        if (toCall == 0)
            row1.Add(InlineKeyboardButton.WithCallbackData(Loc("btn.check"), "poker:check"));
        else
            row1.Add(InlineKeyboardButton.WithCallbackData(string.Format(Loc("btn.call"), toCall), "poker:call"));
        row1.Add(InlineKeyboardButton.WithCallbackData(Loc("btn.fold"), "poker:fold"));

        var row2 = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(Loc("btn.raise"), "poker:raise_menu"),
            InlineKeyboardButton.WithCallbackData(Loc("btn.allin"), "poker:allin"),
        };
        var row3 = new[]
        {
            InlineKeyboardButton.WithCallbackData(Loc("btn.cards"), "poker:cards"),
        };
        return new InlineKeyboardMarkup([row1.ToArray(), row2.ToArray(), row3]);
    }

    private async Task SendError(UpdateContext ctx, long chatId, PokerError error)
    {
        string text = error switch
        {
            PokerError.NotEnoughCoins => string.Format(Loc("err.not_enough_coins"), _opts.BuyIn),
            PokerError.AlreadySeated => Loc("err.already_seated"),
            PokerError.TableNotFound => Loc("err.table_not_found"),
            PokerError.TableFull => Loc("err.table_full"),
            PokerError.HandInProgress => Loc("err.hand_in_progress"),
            PokerError.NotHost => Loc("err.not_host"),
            PokerError.NeedTwo => Loc("err.need_two"),
            PokerError.NoTable => Loc("err.no_table"),
            PokerError.NotYourTurn => Loc("err.not_your_turn"),
            PokerError.CannotCheck => Loc("err.cannot_check"),
            PokerError.RaiseTooSmall => Loc("err.raise_too_small"),
            PokerError.RaiseTooLarge => Loc("err.raise_too_large"),
            PokerError.TableAlreadyExists => Loc("err.table_already_exists"),
            _ => Loc("err.invalid_action"),
        };
        await ctx.Bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
    }

    private string Loc(string key) => localizer.Get("poker", key);

    [LoggerMessage(EventId = 2501, Level = LogLevel.Debug, Message = "poker.showdown.send_failed user={U}")]
    partial void LogPokerShowdownSendFailed(long u, Exception exception);

    [LoggerMessage(EventId = 2502, Level = LogLevel.Debug, Message = "poker.state.send_failed user={U}")]
    partial void LogPokerStateSendFailed(long u, Exception exception);

    [LoggerMessage(EventId = 2503, Level = LogLevel.Warning, Message = "poker.command.failed user={UserId}")]
    partial void LogPokerCommandFailed(long userId, Exception exception);
}
