// ─────────────────────────────────────────────────────────────────────────────
// PickLotteryHandler — handles /picklottery <stake|info|cancel> and /pickjoin.
//
//   /picklottery <stake>          → opens a fresh pool (opener auto-enters)
//   /picklottery info             → status of the pool currently open in this chat
//   /picklottery cancel           → opener cancels and refunds all entries
//   /pickjoin                     → join the open pool (charges stake)
//
// Errors are reported as replies to the user's command. Public events
// (settle / cancellation by sweeper) come from the sweeper, not from here.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Pick;

[Command("/picklottery")]
[Command("/pickjoin")]
public sealed partial class PickLotteryHandler(
    IPickLotteryService service,
    ILocalizer localizer,
    IOptions<PickOptions> options,
    ILogger<PickLotteryHandler> logger) : IUpdateHandler
{
    private PickLotteryOptions LotteryOpts => options.Value.Lottery;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is not { Length: > 0 } text) return;

        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;

        var chatId = msg.Chat.Id;
        var reply = new ReplyParameters { MessageId = msg.MessageId };
        var displayName = msg.From?.Username is { Length: > 0 } un
            ? $"@{un}"
            : msg.From?.FirstName ?? $"User ID: {userId}";

        var (verb, args) = SplitVerbAndArgs(text);

        try
        {
            if (verb == "/pickjoin")
            {
                await HandleJoinAsync(ctx, userId, displayName, chatId, reply);
                return;
            }

            // /picklottery [...]
            var sub = args.Trim();
            if (string.IsNullOrEmpty(sub) || sub.Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                await SendUsageAsync(ctx, chatId, reply);
                return;
            }

            if (sub.Equals("info", StringComparison.OrdinalIgnoreCase))
            {
                await HandleInfoAsync(ctx, chatId, reply);
                return;
            }

            if (sub.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCancelAsync(ctx, userId, chatId, reply);
                return;
            }

            if (!int.TryParse(sub, out var stake) || stake <= 0)
            {
                await SendUsageAsync(ctx, chatId, reply);
                return;
            }

            await HandleOpenAsync(ctx, userId, displayName, chatId, stake, reply);
        }
        catch (Exception ex)
        {
            LogHandleFailed(userId, ex);
            await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
        }
    }

    // ── /picklottery <stake> ─────────────────────────────────────────────────

    private async Task HandleOpenAsync(
        UpdateContext ctx, long userId, string displayName, long chatId, int stake, ReplyParameters reply)
    {
        var result = await service.OpenAsync(userId, displayName, chatId, stake, ctx.Ct);
        switch (result.Status)
        {
            case LotteryOpenStatus.InvalidStake:
            {
                var max = LotteryOpts.MaxStake > 0 ? LotteryOpts.MaxStake.ToString() : "∞";
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("lottery.err.invalid_stake"), Math.Max(1, LotteryOpts.MinStake), max),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }
            case LotteryOpenStatus.NotEnoughCoins:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("lottery.err.no_coins"), result.Balance),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case LotteryOpenStatus.AlreadyOpen:
            {
                var existing = result.Row;
                if (existing is not null)
                {
                    var minutesLeft = Math.Max(1, (int)Math.Ceiling((existing.DeadlineAt - DateTime.UtcNow).TotalMinutes));
                    await ctx.Bot.SendMessage(chatId,
                        string.Format(Loc("lottery.err.already_open"), existing.Stake, minutesLeft),
                        parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                }
                else
                {
                    await ctx.Bot.SendMessage(chatId, Loc("lottery.err.already_open_unknown"),
                        parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                }
                return;
            }
            case LotteryOpenStatus.Ok:
            {
                var row = result.Row!;
                var minutes = Math.Max(1, (int)Math.Ceiling((row.DeadlineAt - DateTime.UtcNow).TotalMinutes));
                var feePct = (int)Math.Round(LotteryOpts.HouseFeePercent * 100);
                var minEntrants = Math.Max(2, LotteryOpts.MinEntrantsToSettle);
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("lottery.opened"), row.Stake, minutes, feePct, minEntrants, result.Balance),
                    parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
                return;
            }
            default:
                await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
        }
    }

    // ── /picklottery info ────────────────────────────────────────────────────

    private async Task HandleInfoAsync(UpdateContext ctx, long chatId, ReplyParameters reply)
    {
        var snap = await service.InfoAsync(chatId, ctx.Ct);
        if (snap is null)
        {
            await ctx.Bot.SendMessage(chatId, Loc("lottery.info.none"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var minutes = Math.Max(0, (int)Math.Ceiling((snap.Row.DeadlineAt - DateTime.UtcNow).TotalMinutes));
        await ctx.Bot.SendMessage(chatId,
            string.Format(Loc("lottery.info.open"),
                snap.Row.Stake, snap.Entrants, snap.PotSoFar, minutes,
                WebUtility.HtmlEncode(snap.Row.OpenerName)),
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    // ── /picklottery cancel ──────────────────────────────────────────────────

    private async Task HandleCancelAsync(UpdateContext ctx, long userId, long chatId, ReplyParameters reply)
    {
        var settle = await service.CancelByOpenerAsync(userId, chatId, ctx.Ct);
        if (settle is null)
        {
            await ctx.Bot.SendMessage(chatId, Loc("lottery.err.cant_cancel"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        await ctx.Bot.SendMessage(chatId,
            string.Format(Loc("lottery.cancelled_by_opener"), settle.Entries.Count, settle.Row.Stake),
            parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
    }

    // ── /pickjoin ────────────────────────────────────────────────────────────

    private async Task HandleJoinAsync(
        UpdateContext ctx, long userId, string displayName, long chatId, ReplyParameters reply)
    {
        var result = await service.JoinAsync(userId, displayName, chatId, ctx.Ct);
        switch (result.Status)
        {
            case LotteryJoinStatus.NoOpenLottery:
                await ctx.Bot.SendMessage(chatId, Loc("lottery.err.no_open"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case LotteryJoinStatus.AlreadyJoined:
                await ctx.Bot.SendMessage(chatId, Loc("lottery.err.already_joined"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            case LotteryJoinStatus.NotEnoughCoins:
            {
                var stake = result.Row?.Stake ?? 0;
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("lottery.err.no_coins_join"), stake, result.Balance),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }
            case LotteryJoinStatus.Ok:
            {
                var row = result.Row!;
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("lottery.joined"),
                        WebUtility.HtmlEncode(displayName), row.Stake, result.Entrants, result.PotSoFar),
                    parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
                return;
            }
            default:
                await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Splits "/pickjoin@Bot rest" or "/picklottery rest" into the verb (lowercased, no @) and the rest.</summary>
    private static (string verb, string args) SplitVerbAndArgs(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '/')
            return (string.Empty, string.Empty);

        var firstSpace = trimmed.IndexOf(' ');
        var head = firstSpace < 0 ? trimmed : trimmed[..firstSpace];
        var rest = firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..];

        var atIdx = head.IndexOf('@');
        if (atIdx > 0) head = head[..atIdx];

        return (head.ToLowerInvariant(), rest);
    }

    private async Task SendUsageAsync(UpdateContext ctx, long chatId, ReplyParameters reply)
    {
        var text = string.Format(
            Loc("lottery.usage"),
            Math.Max(1, LotteryOpts.MinStake),
            LotteryOpts.MaxStake > 0 ? LotteryOpts.MaxStake.ToString() : "∞",
            Math.Max(1, LotteryOpts.DurationSeconds / 60),
            Math.Max(2, LotteryOpts.MinEntrantsToSettle),
            (int)Math.Round(LotteryOpts.HouseFeePercent * 100));
        await ctx.Bot.SendMessage(chatId, text,
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    private string Loc(string key) => localizer.Get("pick", key);

    [LoggerMessage(EventId = 5941, Level = LogLevel.Error,
        Message = "pick.lottery.handle_failed user={UserId}")]
    partial void LogHandleFailed(long userId, Exception ex);
}
