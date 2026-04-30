using System.Net;
using System.Text;
using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Leaderboard;

[Command("/top")]
[Command("/topall")]
[Command("/balance")]
[Command("/daily")]
[Command("/help")]
public sealed class LeaderboardHandler(
    ILeaderboardService service,
    IDailyBonusService dailyBonus,
    ILocalizer localizer,
    IConfiguration configuration,
    IOptions<BotFrameworkOptions> botOptions,
    ILogger<LeaderboardHandler> logger) : IUpdateHandler
{
    private readonly BotFrameworkOptions _bot = botOptions.Value;

    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Text.StartsWith("/help"))
            await HandleHelpAsync(ctx, msg);
        else if (msg.Text.StartsWith("/topall"))
            await HandleTopAllAsync(ctx, msg);
        else if (msg.Text.StartsWith("/top"))
            await HandleTopAsync(ctx, msg);
        else if (msg.Text.StartsWith("/balance"))
            await HandleBalanceAsync(ctx, msg);
        else if (msg.Text.StartsWith("/daily"))
            await HandleDailyAsync(ctx, msg);
    }

    private Task HandleHelpAsync(UpdateContext ctx, Message msg)
    {
        var diceDef = ReadPositiveInt(configuration, "Games:dicecube:DefaultBet", 10);
        var dartsDef = ReadPositiveInt(configuration, "Games:darts:DefaultBet", 10);
        var footballDef = ReadPositiveInt(configuration, "Games:football:DefaultBet", 10);
        var basketDef = ReadPositiveInt(configuration, "Games:basketball:DefaultBet", 10);
        var bowlingDef = ReadPositiveInt(configuration, "Games:bowling:DefaultBet", 10);
        var pickDef = ReadPositiveInt(configuration, "Games:pick:DefaultBet", 10);
        var ticketPrice = ReadPositiveInt(configuration, "Games:pick:Daily:TicketPrice", 50);
        var drawHour = ReadHourOfDay(configuration, "Games:pick:Daily:DrawHourLocal", 18);
        var offsetHours = ReadInt(configuration, "Bot:TelegramDiceDailyLimit:TimezoneOffsetHours", 7);

        var text = string.Format(
            Loc("help"),
            diceDef, dartsDef, footballDef, basketDef, bowlingDef,
            pickDef, ticketPrice, drawHour, FormatUtcOffset(offsetHours));

        return ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private static int ReadInt(IConfiguration cfg, string key, int fallback)
    {
        var s = cfg[key];
        return int.TryParse(s, out var v) ? v : fallback;
    }

    private static int ReadHourOfDay(IConfiguration cfg, string key, int fallback)
    {
        var s = cfg[key];
        if (!int.TryParse(s, out var v)) return fallback;
        return v is >= 0 and <= 23 ? v : fallback;
    }

    private static string FormatUtcOffset(int hours) => hours >= 0 ? $"+{hours}" : hours.ToString();

    private static int ReadPositiveInt(IConfiguration cfg, string key, int fallback)
    {
        var s = cfg[key];
        return int.TryParse(s, out var v) && v > 0 ? v : fallback;
    }

    private async Task HandleTopAsync(UpdateContext ctx, Message msg)
    {
        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var limit = parts.Length > 1 && parts[1] == "full" ? 0 : 15;

        var board = await service.GetTopAsync(limit, msg.Chat.Id, ctx.Ct);
        if (board.Places.Count == 0)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("top.empty"),
                cancellationToken: ctx.Ct);
            return;
        }

        var placeStrings = board.Places.Select((entry, i) =>
        {
            var isFirst = i == 0;
            return entry.Users.Count == 1
                ? $"{entry.Place}. {FormatUser(entry.Users[0], isFirst)}"
                : $"{entry.Place}.\n  - {string.Join("\n  - ", entry.Users.Select(u => FormatUser(u, isFirst)))}";
        });

        var lines = new List<string> { Loc("top.header") };
        lines.AddRange(placeStrings);
        if (board.Truncated) lines.Add(Loc("top.truncated"));
        lines.Add(Loc("top.hidden_reminder"));

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleTopAllAsync(UpdateContext ctx, Message msg)
    {
        var userId = msg.From?.Id ?? 0;
        var reply = new ReplyParameters { MessageId = msg.MessageId };

        if (msg.Chat.Type != ChatType.Private)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("topall.private_only"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        if (userId == 0 || !_bot.Admins.Contains(userId))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("topall.not_admin"),
                parseMode: ParseMode.Html, replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var parts = msg.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mode = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
        var full = mode == "full" || (parts.Length > 2 && parts[2].Equals("full", StringComparison.OrdinalIgnoreCase));

        if (mode is "split" or "by-chat" or "bychat")
        {
            await SendTopAllSplitAsync(ctx, msg, reply, full);
            return;
        }

        await SendTopAllAggregateAsync(ctx, msg, reply, full);
    }

    private async Task SendTopAllAggregateAsync(UpdateContext ctx, Message msg, ReplyParameters reply, bool full)
    {
        var limit = full ? 0 : 30;
        var board = await service.GetGlobalTopAsync(limit, ctx.Ct);

        if (board.Places.Count == 0)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("topall.empty"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var placeStrings = board.Places.Select((entry, i) =>
        {
            var isFirst = i == 0;
            return entry.Users.Count == 1
                ? $"{entry.Place}. {FormatGlobalUser(entry.Users[0], isFirst)}"
                : $"{entry.Place}.\n  - {string.Join("\n  - ", entry.Users.Select(u => FormatGlobalUser(u, isFirst)))}";
        });

        var lines = new List<string>
        {
            string.Format(Loc("topall.header"), board.TotalUsers),
        };
        lines.AddRange(placeStrings);
        if (board.Truncated)
            lines.Add(Loc("topall.truncated"));
        lines.Add(Loc("topall.hidden_reminder"));

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: reply,
            cancellationToken: ctx.Ct);
    }

    private async Task SendTopAllSplitAsync(UpdateContext ctx, Message msg, ReplyParameters reply, bool full)
    {
        var perChatLimit = full ? 0 : 5;
        var board = await service.GetTopByChatAsync(perChatLimit, ctx.Ct);

        if (board.Chats.Count == 0)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("topall.empty"),
                replyParameters: reply, cancellationToken: ctx.Ct);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(Loc("topall.split.header"), board.Chats.Count));
        sb.AppendLine();

        foreach (var chat in board.Chats)
        {
            sb.AppendLine(FormatChatHeader(chat));
            for (var i = 0; i < chat.Places.Count; i++)
            {
                var place = chat.Places[i];
                var isFirst = i == 0;
                if (place.Users.Count == 1)
                {
                    sb.AppendLine($"{place.Place}. {FormatUser(place.Users[0], isFirst)}");
                }
                else
                {
                    sb.AppendLine($"{place.Place}.");
                    foreach (var u in place.Users)
                        sb.AppendLine($"  - {FormatUser(u, isFirst)}");
                }
            }
            sb.AppendLine();
        }

        if (!full)
            sb.AppendLine(Loc("topall.split.hint_full"));

        // Telegram caps message length at 4096 chars; chunk if needed.
        var text = sb.ToString().TrimEnd();
        await SendChunkedAsync(ctx, msg.Chat.Id, text, reply);
    }

    private async Task SendChunkedAsync(UpdateContext ctx, long chatId, string text, ReplyParameters reply)
    {
        const int maxLen = 3800;
        if (text.Length <= maxLen)
        {
            await ctx.Bot.SendMessage(chatId, text,
                parseMode: ParseMode.Html,
                replyParameters: reply,
                cancellationToken: ctx.Ct);
            return;
        }

        var chunks = new List<string>();
        var pending = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (pending.Length + line.Length + 1 > maxLen && pending.Length > 0)
            {
                chunks.Add(pending.ToString().TrimEnd());
                pending.Clear();
            }
            pending.AppendLine(line);
        }
        if (pending.Length > 0) chunks.Add(pending.ToString().TrimEnd());

        for (var i = 0; i < chunks.Count; i++)
        {
            await ctx.Bot.SendMessage(chatId, chunks[i],
                parseMode: ParseMode.Html,
                replyParameters: i == 0 ? reply : null,
                cancellationToken: ctx.Ct);
        }
    }

    private string FormatChatHeader(ChatLeaderboard chat)
    {
        var rawTitle = !string.IsNullOrWhiteSpace(chat.Title)
            ? chat.Title!
            : (chat.ChatType.Equals("private", StringComparison.OrdinalIgnoreCase)
                ? string.Format(Loc("topall.split.private_label"), chat.ChatId)
                : string.Format(Loc("topall.split.unknown_label"), chat.ChatId));
        var title = WebUtility.HtmlEncode(rawTitle);
        var typeBadge = WebUtility.HtmlEncode(chat.ChatType);
        return string.Format(Loc("topall.split.chat_header"), title, typeBadge, chat.ChatId);
    }

    private async Task HandleBalanceAsync(UpdateContext ctx, Message msg)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        var bal = await service.GetBalanceAsync(userId, msg.Chat.Id, displayName, ctx.Ct);

        var text = bal.Visible
            ? string.Format(Loc("balance.visible"), bal.Coins)
            : string.Format(Loc("balance.hidden"), bal.Coins);

        await ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleDailyAsync(UpdateContext ctx, Message msg)
    {
        var userId = msg.From?.Id ?? 0;
        if (userId == 0) return;
        var displayName = msg.From?.Username ?? msg.From?.FirstName ?? $"User ID: {userId}";

        DailyBonusClaimResult r;
        try
        {
            r = await dailyBonus.TryClaimAsync(userId, msg.Chat.Id, displayName, ctx.Ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "daily command failed user={UserId} scope={Scope}", userId, msg.Chat.Id);
            await ctx.Bot.SendMessage(msg.Chat.Id, Loc("daily.failed"),
                parseMode: ParseMode.Html,
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var text = r.Status switch
        {
            DailyBonusClaimStatus.Claimed => string.Format(Loc("daily.claimed"), r.BonusCoins, r.NewBalance),
            DailyBonusClaimStatus.AlreadyClaimedToday => Loc("daily.already"),
            DailyBonusClaimStatus.Disabled => Loc("daily.disabled"),
            DailyBonusClaimStatus.IneligibleEmptyBalance => Loc("daily.empty_balance"),
            DailyBonusClaimStatus.IneligiblePercentRoundsToZero => Loc("daily.too_small"),
            _ => Loc("daily.disabled"),
        };

        await ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private static string FormatUser(LeaderboardUser user, bool isFirstPlace)
    {
        var crown = isFirstPlace ? "👑 " : "";
        var safeName = WebUtility.HtmlEncode(user.DisplayName ?? "Unknown").Replace("@", "@\u200B");
        return $"{crown}{safeName} - {user.Coins}";
    }

    private static string FormatGlobalUser(GlobalLeaderboardUser user, bool isFirstPlace)
    {
        var crown = isFirstPlace ? "👑 " : "";
        var safeName = WebUtility.HtmlEncode(user.DisplayName ?? "Unknown").Replace("@", "@\u200B");
        return user.ChatCount > 1
            ? $"{crown}{safeName} - {user.TotalCoins} (×{user.ChatCount})"
            : $"{crown}{safeName} - {user.TotalCoins}";
    }

    private string Loc(string key) => localizer.Get("leaderboard", key);
}
