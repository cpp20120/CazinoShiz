// ─────────────────────────────────────────────────────────────────────────────
// PickDailyLotteryHandler — /dailylottery [info|buy [N]|history|<N>]
//
// Subcommand resolution:
//   • no args                       → info
//   • "info"                        → info (today's pool stats)
//   • "buy" [N]                     → buy N tickets (default 1)
//   • "history" [N]                 → last N settled draws (default = HistoryLimit option)
//   • <positive number>             → shorthand: buy that many tickets
//
// Sweeper posts public draw/cancel results on the chat — this handler only
// emits replies to the user's command.
// ─────────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Text;
using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Pick;

[Command("/dailylottery")]
public sealed partial class PickDailyLotteryHandler(
    IPickDailyLotteryService service,
    ILocalizer localizer,
    IOptions<PickOptions> options,
    ILogger<PickDailyLotteryHandler> logger) : IUpdateHandler
{
    private PickDailyLotteryOptions Opts => options.Value.Daily;

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

        var args = StripCommandPrefix(text);

        try
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await HandleInfoAsync(ctx, userId, chatId, reply);
                return;
            }

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var head = parts[0].ToLowerInvariant();

            switch (head)
            {
                case "help":
                    await SendUsageAsync(ctx, chatId, reply);
                    return;
                case "info":
                    await HandleInfoAsync(ctx, userId, chatId, reply);
                    return;
                case "buy":
                {
                    var count = 1;
                    if (parts.Length > 1 && int.TryParse(parts[1], out var n)) count = n;
                    await HandleBuyAsync(ctx, userId, displayName, chatId, count, reply);
                    return;
                }
                case "history":
                {
                    var lim = Math.Max(1, Opts.HistoryLimit);
                    if (parts.Length > 1 && int.TryParse(parts[1], out var n)) lim = n;
                    await HandleHistoryAsync(ctx, chatId, lim, reply);
                    return;
                }
                default:
                {
                    if (int.TryParse(head, out var n) && n > 0)
                    {
                        await HandleBuyAsync(ctx, userId, displayName, chatId, n, reply);
                        return;
                    }
                    await SendUsageAsync(ctx, chatId, reply);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogHandleFailed(userId, ex);
            await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
        }
    }

    // ── info ─────────────────────────────────────────────────────────────────

    private async Task HandleInfoAsync(UpdateContext ctx, long viewerId, long chatId, ReplyParameters reply)
    {
        var snap = await service.InfoAsync(chatId, viewerId, ctx.Ct);
        if (snap is null)
        {
            await ctx.Bot.SendMessage(chatId,
                string.Format(Loc("daily.info.empty"), Math.Max(1, Opts.TicketPrice)),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var minutesLeft = Math.Max(0, (int)Math.Ceiling((snap.Row.DeadlineAt - DateTime.UtcNow).TotalMinutes));
        var hoursLeft = minutesLeft / 60;
        var minsTail = minutesLeft % 60;
        var timeLeftText = hoursLeft > 0
            ? string.Format(Loc("daily.time.h_m"), hoursLeft, minsTail)
            : string.Format(Loc("daily.time.m"), minsTail);

        var winChancePct = snap.TicketsTotal > 0
            ? Math.Round(100.0 * snap.ViewerTickets / snap.TicketsTotal, 1)
            : 0.0;

        var sb = new StringBuilder();
        sb.AppendFormat(Loc("daily.info.header"),
            snap.Row.TicketPrice, snap.Row.DayLocal.ToString("yyyy-MM-dd"));
        sb.Append('\n');
        sb.AppendFormat(Loc("daily.info.stats"),
            snap.TicketsTotal, snap.DistinctUsers, snap.PotTotal, timeLeftText);
        sb.Append('\n');
        sb.AppendFormat(Loc("daily.info.your_tickets"),
            snap.ViewerTickets, winChancePct.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append('\n');

        if (snap.TopHolders.Count > 0)
        {
            sb.Append('\n');
            sb.AppendLine(Loc("daily.info.top_header"));
            for (var i = 0; i < snap.TopHolders.Count; i++)
            {
                var t = snap.TopHolders[i];
                sb.AppendFormat(Loc("daily.info.top_row"),
                    i + 1, WebUtility.HtmlEncode(t.DisplayName), t.TicketCount);
                sb.Append('\n');
            }
        }

        sb.Append('\n');
        sb.AppendFormat(Loc("daily.info.buy_hint"), snap.Row.TicketPrice);

        await ctx.Bot.SendMessage(chatId, sb.ToString().TrimEnd(),
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    // ── buy ──────────────────────────────────────────────────────────────────

    private async Task HandleBuyAsync(
        UpdateContext ctx, long userId, string displayName, long chatId, int count, ReplyParameters reply)
    {
        var result = await service.BuyAsync(userId, displayName, chatId, count, ctx.Ct);
        switch (result.Status)
        {
            case DailyBuyStatus.InvalidCount:
                await ctx.Bot.SendMessage(chatId, Loc("daily.err.invalid_count"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DailyBuyStatus.OverPerCommandCap:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("daily.err.over_command_cap"), Math.Max(1, Opts.MaxTicketsPerBuyCommand)),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DailyBuyStatus.OverDailyCap:
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("daily.err.over_daily_cap"), Opts.MaxTicketsPerUserPerDay, result.TotalUserTickets),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DailyBuyStatus.NotEnoughCoins:
            {
                var price = result.Row?.TicketPrice ?? Opts.TicketPrice;
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("daily.err.no_coins"), count, price, count * price, result.Balance),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
            }

            case DailyBuyStatus.DayAlreadyClosing:
                await ctx.Bot.SendMessage(chatId, Loc("daily.err.day_closing"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;

            case DailyBuyStatus.Ok:
            {
                var row = result.Row!;
                await ctx.Bot.SendMessage(chatId,
                    string.Format(Loc("daily.bought"),
                        WebUtility.HtmlEncode(displayName),
                        result.TicketsBought, result.TotalUserTickets,
                        result.TicketsBought * row.TicketPrice,
                        result.TotalTickets, result.PotTotal, result.Balance),
                    parseMode: ParseMode.Html, cancellationToken: ctx.Ct);
                return;
            }

            default:
                await ctx.Bot.SendMessage(chatId, Loc("err.generic"),
                    parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
                return;
        }
    }

    // ── history ──────────────────────────────────────────────────────────────

    private async Task HandleHistoryAsync(UpdateContext ctx, long chatId, int limit, ReplyParameters reply)
    {
        var history = await service.HistoryAsync(chatId, limit, ctx.Ct);
        if (history.Count == 0)
        {
            await ctx.Bot.SendMessage(chatId, Loc("daily.history.empty"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Loc("daily.history.header"));
        foreach (var row in history)
        {
            var day = row.DayLocal.ToString("yyyy-MM-dd");
            if (row.Status == "settled" && row.WinnerId is not null)
            {
                var winnerLabel = string.IsNullOrEmpty(row.WinnerName)
                    ? $"User ID: {row.WinnerId}"
                    : WebUtility.HtmlEncode(row.WinnerName);
                sb.AppendFormat(Loc("daily.history.row_settled"),
                    day, winnerLabel,
                    row.PotTotal ?? 0, row.Payout ?? 0,
                    row.TicketCount ?? 0);
                sb.Append('\n');
            }
            else
            {
                sb.AppendFormat(Loc("daily.history.row_cancelled"), day);
                sb.Append('\n');
            }
        }

        await ctx.Bot.SendMessage(chatId, sb.ToString().TrimEnd(),
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task SendUsageAsync(UpdateContext ctx, long chatId, ReplyParameters reply)
    {
        var feePct = (int)Math.Round(Opts.HouseFeePercent * 100);
        var capText = Opts.MaxTicketsPerUserPerDay > 0
            ? Opts.MaxTicketsPerUserPerDay.ToString()
            : "∞";
        var drawTime = FormatDrawTime();
        await ctx.Bot.SendMessage(chatId,
            string.Format(
                Loc("daily.usage"),
                Math.Max(1, Opts.TicketPrice),
                Math.Max(1, Opts.MaxTicketsPerBuyCommand),
                capText,
                feePct,
                drawTime),
            parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
    }

    /// <summary>e.g. "18:00 (UTC+7)" — used in the usage hint.</summary>
    private string FormatDrawTime()
    {
        var hoursOffset = service.OffsetHours;
        var sign = hoursOffset >= 0 ? "+" : "";
        return $"{service.DrawHourLocal:00}:00 (UTC{sign}{hoursOffset})";
    }

    private static string StripCommandPrefix(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '/') return string.Empty;
        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0 ? string.Empty : trimmed[(firstSpace + 1)..].Trim();
    }

    private string Loc(string key) => localizer.Get("pick", key);

    [LoggerMessage(EventId = 5961, Level = LogLevel.Error,
        Message = "pick.daily.handle_failed user={UserId}")]
    partial void LogHandleFailed(long userId, Exception ex);
}
