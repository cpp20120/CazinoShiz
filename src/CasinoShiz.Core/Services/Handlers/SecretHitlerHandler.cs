using CasinoShiz.Configuration;
using CasinoShiz.Data.Entities;
using CasinoShiz.Helpers;
using CasinoShiz.Services.Pipeline;
using CasinoShiz.Services.SecretHitler.Application;
using CasinoShiz.Services.SecretHitler.Domain;
using CasinoShiz.Services.SecretHitler.Presentation;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CasinoShiz.Services.Handlers;

[Command("/sh")]
[CallbackPrefix("sh:")]
public sealed partial class SecretHitlerHandler(
    SecretHitlerService service,
    IOptions<BotOptions> options,
    ILogger<SecretHitlerHandler> logger) : IUpdateHandler
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
            await bot.SendMessage(msg.Chat.Id, Locales.ShOnlyPrivate(),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId }, cancellationToken: ct);
            return;
        }

        var command = ShCommandParser.ParseText(msg.Text);
        await DispatchTextAsync(bot, msg, command, ct);
    }

    private async Task DispatchTextAsync(ITelegramBotClient bot, Message msg, ShCommand command, CancellationToken ct)
    {
        var userId = msg.From!.Id;
        var chatId = msg.Chat.Id;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (command)
        {
            case ShCommand.Create:
                await ExecuteCreate(bot, userId, displayName, chatId, ct);
                break;
            case ShCommand.Join j:
                await ExecuteJoin(bot, userId, displayName, chatId, j.Code, ct);
                break;
            case ShCommand.JoinMissingCode:
                await bot.SendMessage(chatId, "Укажи код: /sh join КОД", cancellationToken: ct);
                break;
            case ShCommand.Start:
                await ExecuteStart(bot, userId, chatId, ct);
                break;
            case ShCommand.Leave:
                await ExecuteLeave(bot, userId, chatId, ct);
                break;
            case ShCommand.Status:
                await ExecuteStatus(bot, userId, chatId, ct);
                break;
            default:
                await bot.SendMessage(chatId, Locales.ShUsage(),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ct);
                break;
        }
    }

    private async Task DispatchCallbackAsync(ITelegramBotClient bot, CallbackQuery cbq, CancellationToken ct)
    {
        try { await bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ct); } catch { }

        var command = ShCommandParser.ParseCallback(cbq.Data);
        if (command == null) return;

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;

        switch (command)
        {
            case ShCommand.Nominate n:
                await ExecuteNominate(bot, userId, chatId, n.ChancellorPosition, ct);
                break;
            case ShCommand.Vote v:
                await ExecuteVote(bot, userId, chatId, v.Ja, ct);
                break;
            case ShCommand.PresidentDiscard d:
                await ExecutePresidentDiscard(bot, userId, chatId, d.Index, ct);
                break;
            case ShCommand.ChancellorEnact e:
                await ExecuteChancellorEnact(bot, userId, chatId, e.Index, ct);
                break;
        }
    }

    private async Task ExecuteCreate(ITelegramBotClient bot, long userId, string displayName, long chatId, CancellationToken ct)
    {
        var r = await service.CreateGameAsync(userId, displayName, chatId, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        await bot.SendMessage(chatId, Locales.ShGameCreated(r.InviteCode, r.BuyIn),
            parseMode: ParseMode.Html, cancellationToken: ct);
    }

    private async Task ExecuteJoin(ITelegramBotClient bot, long userId, string displayName, long chatId, string code, CancellationToken ct)
    {
        var r = await service.JoinGameAsync(userId, displayName, chatId, code, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        await bot.SendMessage(chatId, Locales.ShJoined(code.ToUpperInvariant(), r.Joined, r.Max),
            parseMode: ParseMode.Html, cancellationToken: ct);
        if (r.Snapshot != null) await BroadcastLobbyAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteStart(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var r = await service.StartGameAsync(userId, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot != null)
        {
            await SendRoleCardsAsync(bot, r.Snapshot, ct);
            await BroadcastBoardAsync(bot, r.Snapshot, ct);
        }
    }

    private async Task ExecuteLeave(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var r = await service.LeaveAsync(userId, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        var msg = r.GameClosed ? $"{Locales.ShLeft()}\n{Locales.ShGameClosed()}" : Locales.ShLeft();
        await bot.SendMessage(chatId, msg, parseMode: ParseMode.Html, cancellationToken: ct);
        if (r.Snapshot != null && !r.GameClosed) await BroadcastLobbyAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteStatus(ITelegramBotClient bot, long userId, long chatId, CancellationToken ct)
    {
        var (snap, me) = await service.FindMyGameAsync(userId, ct);
        if (snap == null || me == null) { await SendError(bot, chatId, ShError.NotInGame, ct); return; }
        if (snap.Game.Status == ShStatus.Lobby) await BroadcastLobbyAsync(bot, snap, ct);
        else await SendOrEditBoardAsync(bot, snap, me, ct);
    }

    private async Task ExecuteNominate(ITelegramBotClient bot, long userId, long chatId, int chancellorPosition, CancellationToken ct)
    {
        var r = await service.NominateAsync(userId, chancellorPosition, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot != null) await BroadcastBoardAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteVote(ITelegramBotClient bot, long userId, long chatId, bool ja, CancellationToken ct)
    {
        var r = await service.VoteAsync(userId, ja ? ShVote.Ja : ShVote.Nein, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot == null) return;

        if (r.After != null)
        {
            await BroadcastVoteResolutionAsync(bot, r.Snapshot, r.After, ct);
        }
        await BroadcastBoardAsync(bot, r.Snapshot, ct);

        if (r.Snapshot.Game.Status == ShStatus.Completed)
            await BroadcastEndAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecutePresidentDiscard(ITelegramBotClient bot, long userId, long chatId, int index, CancellationToken ct)
    {
        var r = await service.PresidentDiscardAsync(userId, index, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot != null) await BroadcastBoardAsync(bot, r.Snapshot, ct);
    }

    private async Task ExecuteChancellorEnact(ITelegramBotClient bot, long userId, long chatId, int index, CancellationToken ct)
    {
        var r = await service.ChancellorEnactAsync(userId, index, ct);
        if (r.Error != ShError.None) { await SendError(bot, chatId, r.Error, ct); return; }
        if (r.Snapshot == null || r.After == null) return;

        var enactedMsg = Locales.ShPolicyEnacted(r.After.Enacted == ShPolicy.Liberal);
        foreach (var p in r.Snapshot.Players.Where(p => p.ChatId != 0))
        {
            try { await bot.SendMessage(p.ChatId, enactedMsg, parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        }

        await BroadcastBoardAsync(bot, r.Snapshot, ct);
        if (r.Snapshot.Game.Status == ShStatus.Completed)
            await BroadcastEndAsync(bot, r.Snapshot, ct);
    }

    private async Task BroadcastLobbyAsync(ITelegramBotClient bot, ShGameSnapshot snap, CancellationToken ct)
    {
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
            await SendOrEditBoardAsync(bot, snap, p, ct);
    }

    private async Task BroadcastBoardAsync(ITelegramBotClient bot, ShGameSnapshot snap, CancellationToken ct)
    {
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
            await SendOrEditBoardAsync(bot, snap, p, ct);
    }

    private async Task BroadcastVoteResolutionAsync(
        ITelegramBotClient bot, ShGameSnapshot snap, ShAfterVoteResult after, CancellationToken ct)
    {
        string? msg = null;
        switch (after.Kind)
        {
            case ShAfterVoteKind.ElectionPassed:
            {
                var chancellor = snap.Players.First(p => p.Position == snap.Game.LastElectedChancellorPosition);
                msg = $"{ShStateRenderer.RenderVoteReveal(snap.Players)}\n\n{Locales.ShElectionPassed(after.JaVotes, after.NeinVotes, chancellor.DisplayName)}";
                break;
            }
            case ShAfterVoteKind.ElectionFailed:
                msg = $"{ShStateRenderer.RenderVoteReveal(snap.Players)}\n\n{Locales.ShElectionFailed(after.JaVotes, after.NeinVotes, snap.Game.ElectionTracker)}";
                break;
            case ShAfterVoteKind.HitlerElectedWin:
            {
                var hitler = snap.Players.First(p => p.Role == ShRole.Hitler);
                msg = $"{ShStateRenderer.RenderVoteReveal(snap.Players)}\n\n{Locales.ShHitlerElectedWin(hitler.DisplayName)}";
                break;
            }
        }
        if (msg == null) return;
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
        {
            try { await bot.SendMessage(p.ChatId, msg, parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        }
    }

    private async Task BroadcastEndAsync(ITelegramBotClient bot, ShGameSnapshot snap, CancellationToken ct)
    {
        var text = ShStateRenderer.RenderEndSummary(snap.Game, snap.Players);
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
        {
            try { await bot.SendMessage(p.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ct); } catch { }
        }
    }

    private async Task SendRoleCardsAsync(ITelegramBotClient bot, ShGameSnapshot snap, CancellationToken ct)
    {
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
        {
            var text = ShStateRenderer.RenderRoleCard(p, snap.Players, snap.Players.Count);
            try { await bot.SendMessage(p.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ct); }
            catch (Exception ex) { LogShRoleSendFailed(p.UserId, ex); }
        }
    }

    private async Task SendOrEditBoardAsync(ITelegramBotClient bot, ShGameSnapshot snap, SecretHitlerPlayer viewer, CancellationToken ct)
    {
        var text = ShStateRenderer.RenderBoard(snap.Game, snap.Players);
        var markup = ShStateRenderer.BuildBoardMarkup(snap.Game, viewer, snap.Players);

        if (viewer.StateMessageId.HasValue)
        {
            try
            {
                await bot.EditMessageText(viewer.ChatId, viewer.StateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch { }
        }

        try
        {
            var sent = await bot.SendMessage(viewer.ChatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
            await service.SetStateMessageIdAsync(viewer.UserId, sent.MessageId, ct);
        }
        catch (Exception ex)
        {
            LogShBoardSendFailed(viewer.UserId, ex);
        }
    }

    private async Task SendError(ITelegramBotClient bot, long chatId, ShError error, CancellationToken ct)
    {
        string text = error switch
        {
            ShError.NotEnoughCoins => Locales.ShNotEnoughCoins(_opts.SecretHitlerBuyIn),
            ShError.AlreadyInGame => Locales.ShAlreadyInGame(),
            ShError.GameNotFound => Locales.ShGameNotFound(),
            ShError.GameFull => Locales.ShGameFull(),
            ShError.GameInProgress => Locales.ShGameInProgress(),
            ShError.NotHost => Locales.ShNotHost(),
            ShError.NotInGame => Locales.ShNotInGame(),
            ShError.NotEnoughPlayers => Locales.ShNotEnoughPlayers(),
            ShError.WrongPhase => Locales.ShWrongPhase(),
            ShError.NotPresident => Locales.ShNotPresident(),
            ShError.NotChancellor => Locales.ShNotChancellor(),
            ShError.InvalidTarget => Locales.ShInvalidTarget(),
            ShError.TermLimited => Locales.ShTermLimited(),
            ShError.AlreadyVoted => Locales.ShAlreadyVoted(),
            ShError.InvalidPolicy => Locales.ShInvalidPolicy(),
            _ => "Ошибка.",
        };
        await bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ct);
    }

    [LoggerMessage(LogLevel.Debug, "sh.board.send_failed user={U}")]
    partial void LogShBoardSendFailed(long u, Exception exception);

    [LoggerMessage(LogLevel.Debug, "sh.role.send_failed user={U}")]
    partial void LogShRoleSendFailed(long u, Exception exception);
}
