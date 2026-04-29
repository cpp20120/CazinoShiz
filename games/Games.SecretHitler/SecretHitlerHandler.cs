using BotFramework.Host;
using BotFramework.Sdk;
using Games.SecretHitler.Domain;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.SecretHitler;

[Command("/sh")]
[CallbackPrefix("sh:")]
public sealed partial class SecretHitlerHandler(
    ISecretHitlerService service,
    ILocalizer localizer,
    IOptions<SecretHitlerOptions> options,
    ILogger<SecretHitlerHandler> logger) : IUpdateHandler
{
    private readonly SecretHitlerOptions _opts = options.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery != null)
        {
            await DispatchCallbackAsync(ctx, ctx.Update.CallbackQuery);
            return;
        }

        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Chat.Type is not (ChatType.Private or ChatType.Group or ChatType.Supergroup))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("err.unsupported_chat"),
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var command = SecretHitlerCommandParser.ParseText(msg.Text);
        await DispatchTextAsync(ctx, msg, command);
    }

    private async Task DispatchTextAsync(UpdateContext ctx, Message msg, SecretHitlerCommand command)
    {
        var userId = msg.From!.Id;
        var chatId = msg.Chat.Id;
        var playerChatId = msg.Chat.Type == ChatType.Private ? chatId : userId;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        switch (command)
        {
            case SecretHitlerCommand.Create:
                await ExecuteCreate(ctx, userId, displayName, chatId, playerChatId);
                break;
            case SecretHitlerCommand.Join j:
                await ExecuteJoin(ctx, userId, displayName, chatId, playerChatId, j.Code);
                break;
            case SecretHitlerCommand.JoinMissingCode:
                await ctx.Bot.SendMessage(chatId, Loc("err.join_missing_code"), cancellationToken: ctx.Ct);
                break;
            case SecretHitlerCommand.Start:
                await ExecuteStart(ctx, userId, chatId);
                break;
            case SecretHitlerCommand.Leave:
                await ExecuteLeave(ctx, userId, chatId);
                break;
            case SecretHitlerCommand.Status:
                await ExecuteStatus(ctx, userId, chatId);
                break;
            default:
                await ctx.Bot.SendMessage(chatId, Loc("usage"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                break;
        }
    }

    private async Task DispatchCallbackAsync(UpdateContext ctx, CallbackQuery cbq)
    {
        try { await ctx.Bot.AnswerCallbackQuery(cbq.Id, cancellationToken: ctx.Ct); } catch { }

        var command = SecretHitlerCommandParser.ParseCallback(cbq.Data);
        if (command == null) return;

        var userId = cbq.From.Id;
        var chatId = cbq.Message?.Chat.Id ?? userId;
        var playerChatId = cbq.Message?.Chat.Type == ChatType.Private ? chatId : userId;
        var displayName = cbq.From.Username ?? cbq.From.FirstName ?? $"User ID: {userId}";

        switch (command)
        {
            case SecretHitlerCommand.Join j:
                await ExecuteJoin(ctx, userId, displayName, chatId, playerChatId, j.Code);
                break;
            case SecretHitlerCommand.Start:
                await ExecuteStart(ctx, userId, chatId);
                break;
            case SecretHitlerCommand.Nominate n:
                await ExecuteNominate(ctx, userId, chatId, n.ChancellorPosition);
                break;
            case SecretHitlerCommand.Vote v:
                await ExecuteVote(ctx, userId, chatId, v.Ja);
                break;
            case SecretHitlerCommand.PresidentDiscard d:
                await ExecutePresidentDiscard(ctx, userId, chatId, d.Index);
                break;
            case SecretHitlerCommand.ChancellorEnact e:
                await ExecuteChancellorEnact(ctx, userId, chatId, e.Index);
                break;
        }
    }

    private async Task ExecuteCreate(
        UpdateContext ctx, long userId, string displayName, long publicChatId, long playerChatId)
    {
        var r = await service.CreateGameAsync(userId, displayName, publicChatId, playerChatId, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, publicChatId, r.Error); return; }
        await ctx.Bot.SendMessage(publicChatId, string.Format(Loc("created"), r.InviteCode, r.BuyIn),
            parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        var (snap, _) = await service.FindMyGameAsync(userId, ctx.Ct);
        if (snap != null) await BroadcastLobbyAsync(ctx, snap);
    }

    private async Task ExecuteJoin(
        UpdateContext ctx, long userId, string displayName, long chatId, long playerChatId, string code)
    {
        var r = await service.JoinGameAsync(userId, displayName, playerChatId, code, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        await ctx.Bot.SendMessage(chatId, string.Format(Loc("joined"), code.ToUpperInvariant(), r.Joined, r.Max),
            parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        if (r.Snapshot != null) await BroadcastLobbyAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteStart(UpdateContext ctx, long userId, long chatId)
    {
        var r = await service.StartGameAsync(userId, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot != null)
        {
            await SendRoleCardsAsync(ctx, r.Snapshot);
            await BroadcastBoardAsync(ctx, r.Snapshot);
        }
    }

    private async Task ExecuteLeave(UpdateContext ctx, long userId, long chatId)
    {
        var r = await service.LeaveAsync(userId, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        var msg = r.GameClosed ? $"{Loc("left")}\n{Loc("game_closed")}" : Loc("left");
        await ctx.Bot.SendMessage(chatId, msg, parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        if (r.Snapshot != null && !r.GameClosed) await BroadcastLobbyAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteStatus(UpdateContext ctx, long userId, long chatId)
    {
        var (snap, me) = await service.FindMyGameAsync(userId, ctx.Ct);
        if (snap == null || me == null) { await SendError(ctx, chatId, ShError.NotInGame); return; }
        if (snap.Game.Status == ShStatus.Lobby) await BroadcastLobbyAsync(ctx, snap);
        else if (chatId == snap.Game.ChatId) await SendOrEditPublicBoardAsync(ctx, snap);
        else await SendOrEditPrivateBoardAsync(ctx, snap, me);
    }

    private async Task ExecuteNominate(UpdateContext ctx, long userId, long chatId, int chancellorPosition)
    {
        var r = await service.NominateAsync(userId, chancellorPosition, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot != null) await BroadcastBoardAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteVote(UpdateContext ctx, long userId, long chatId, bool ja)
    {
        var r = await service.VoteAsync(userId, ja ? ShVote.Ja : ShVote.Nein, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot == null) return;

        if (r.After != null)
            await BroadcastVoteResolutionAsync(ctx, r.Snapshot, r.After);

        await BroadcastBoardAsync(ctx, r.Snapshot);

        if (r.Snapshot.Game.Status == ShStatus.Completed)
            await BroadcastEndAsync(ctx, r.Snapshot);
    }

    private async Task ExecutePresidentDiscard(UpdateContext ctx, long userId, long chatId, int index)
    {
        var r = await service.PresidentDiscardAsync(userId, index, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot != null) await BroadcastBoardAsync(ctx, r.Snapshot);
    }

    private async Task ExecuteChancellorEnact(UpdateContext ctx, long userId, long chatId, int index)
    {
        var r = await service.ChancellorEnactAsync(userId, index, ctx.Ct);
        if (r.Error != ShError.None) { await SendError(ctx, chatId, r.Error); return; }
        if (r.Snapshot == null || r.After == null) return;

        var enactedKey = r.After.Enacted == ShPolicy.Liberal ? "policy.enacted_liberal" : "policy.enacted_fascist";
        var enactedMsg = Loc(enactedKey);
        await SendPublicAnnouncementAsync(ctx, r.Snapshot, enactedMsg);

        await BroadcastBoardAsync(ctx, r.Snapshot);
        if (r.Snapshot.Game.Status == ShStatus.Completed)
            await BroadcastEndAsync(ctx, r.Snapshot);
    }

    private async Task BroadcastLobbyAsync(UpdateContext ctx, ShGameSnapshot snap)
    {
        await SendOrEditPublicBoardAsync(ctx, snap);
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
            await SendOrEditPrivateBoardAsync(ctx, snap, p);
    }

    private async Task BroadcastBoardAsync(UpdateContext ctx, ShGameSnapshot snap)
    {
        await SendOrEditPublicBoardAsync(ctx, snap);
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
            await SendOrEditPrivateBoardAsync(ctx, snap, p);
    }

    private async Task BroadcastVoteResolutionAsync(UpdateContext ctx, ShGameSnapshot snap, ShAfterVoteResult after)
    {
        string? msg = null;
        switch (after.Kind)
        {
            case ShAfterVoteKind.ElectionPassed:
            {
                var chancellor = snap.Players.First(p => p.Position == snap.Game.LastElectedChancellorPosition);
                msg = $"{SecretHitlerStateRenderer.RenderVoteReveal(snap.Players, localizer)}\n\n" +
                      string.Format(Loc("vote.election_passed"), after.JaVotes, after.NeinVotes, chancellor.DisplayName);
                break;
            }
            case ShAfterVoteKind.ElectionFailed:
                msg = $"{SecretHitlerStateRenderer.RenderVoteReveal(snap.Players, localizer)}\n\n" +
                      string.Format(Loc("vote.election_failed"), after.JaVotes, after.NeinVotes, snap.Game.ElectionTracker);
                break;
            case ShAfterVoteKind.HitlerElectedWin:
            {
                var hitler = snap.Players.First(p => p.Role == ShRole.Hitler);
                msg = $"{SecretHitlerStateRenderer.RenderVoteReveal(snap.Players, localizer)}\n\n" +
                      string.Format(Loc("vote.hitler_elected_win"), hitler.DisplayName);
                break;
            }
        }
        if (msg == null) return;
        await SendPublicAnnouncementAsync(ctx, snap, msg);
    }

    private async Task BroadcastEndAsync(UpdateContext ctx, ShGameSnapshot snap)
    {
        var text = SecretHitlerStateRenderer.RenderEndSummary(snap.Game, snap.Players, localizer);
        await SendPublicAnnouncementAsync(ctx, snap, text);
    }

    private async Task SendRoleCardsAsync(UpdateContext ctx, ShGameSnapshot snap)
    {
        foreach (var p in snap.Players.Where(p => p.ChatId != 0))
        {
            var text = SecretHitlerStateRenderer.RenderRoleCard(p, snap.Players, snap.Players.Count, localizer);
            try { await ctx.Bot.SendMessage(p.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ctx.Ct); }
            catch (Exception ex) { LogShRoleSendFailed(p.UserId, ex); }
        }
    }

    private async Task SendOrEditPublicBoardAsync(UpdateContext ctx, ShGameSnapshot snap)
    {
        var text = SecretHitlerStateRenderer.RenderBoard(snap.Game, snap.Players, localizer);
        var markup = SecretHitlerStateRenderer.BuildPublicMarkup(snap.Game, snap.Players, localizer);

        if (snap.Game.StateMessageId.HasValue)
        {
            try
            {
                await ctx.Bot.EditMessageText(snap.Game.ChatId, snap.Game.StateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch { }
        }

        try
        {
            var sent = await ctx.Bot.SendMessage(snap.Game.ChatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
            await service.SetPublicStateMessageIdAsync(snap.Game.InviteCode, sent.MessageId, ctx.Ct);
        }
        catch (Exception ex)
        {
            LogShPublicBoardSendFailed(snap.Game.InviteCode, ex);
        }
    }

    private async Task SendOrEditPrivateBoardAsync(UpdateContext ctx, ShGameSnapshot snap, SecretHitlerPlayer viewer)
    {
        var text = SecretHitlerStateRenderer.RenderBoard(snap.Game, snap.Players, localizer);
        var markup = SecretHitlerStateRenderer.BuildBoardMarkup(snap.Game, viewer, snap.Players, localizer);

        if (viewer.StateMessageId.HasValue)
        {
            try
            {
                await ctx.Bot.EditMessageText(viewer.ChatId, viewer.StateMessageId.Value, text,
                    parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
                return;
            }
            catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified")) { return; }
            catch { }
        }

        try
        {
            var sent = await ctx.Bot.SendMessage(viewer.ChatId, text,
                parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ctx.Ct);
            await service.SetStateMessageIdAsync(viewer.UserId, sent.MessageId, ctx.Ct);
        }
        catch (Exception ex)
        {
            LogShBoardSendFailed(viewer.UserId, ex);
        }
    }

    private async Task SendPublicAnnouncementAsync(UpdateContext ctx, ShGameSnapshot snap, string text)
    {
        try
        {
            await ctx.Bot.SendMessage(snap.Game.ChatId, text, parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
        }
        catch (Exception ex)
        {
            LogShPublicBoardSendFailed(snap.Game.InviteCode, ex);
        }
    }

    private async Task SendError(UpdateContext ctx, long chatId, ShError error)
    {
        var text = error switch
        {
            ShError.NotEnoughCoins => string.Format(Loc("err.not_enough_coins"), _opts.BuyIn),
            ShError.AlreadyInGame => Loc("err.already_in_game"),
            ShError.GameNotFound => Loc("err.game_not_found"),
            ShError.GameFull => Loc("err.game_full"),
            ShError.GameInProgress => Loc("err.game_in_progress"),
            ShError.NotHost => Loc("err.not_host"),
            ShError.NotInGame => Loc("err.not_in_game"),
            ShError.NotEnoughPlayers => Loc("err.not_enough_players"),
            ShError.WrongPhase => Loc("err.wrong_phase"),
            ShError.NotPresident => Loc("err.not_president"),
            ShError.NotChancellor => Loc("err.not_chancellor"),
            ShError.InvalidTarget => Loc("err.invalid_target"),
            ShError.TermLimited => Loc("err.term_limited"),
            ShError.AlreadyVoted => Loc("err.already_voted"),
            ShError.InvalidPolicy => Loc("err.invalid_policy"),
            _ => Loc("err.generic"),
        };
        await ctx.Bot.SendMessage(chatId, text, parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
    }

    private string Loc(string key) => localizer.Get("sh", key);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Debug, Message = "sh.board.send_failed user={U}")]
    partial void LogShBoardSendFailed(long u, Exception exception);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Debug, Message = "sh.role.send_failed user={U}")]
    partial void LogShRoleSendFailed(long u, Exception exception);

    [LoggerMessage(EventId = 2603, Level = LogLevel.Debug, Message = "sh.public_board.send_failed code={Code}")]
    partial void LogShPublicBoardSendFailed(string code, Exception exception);
}
