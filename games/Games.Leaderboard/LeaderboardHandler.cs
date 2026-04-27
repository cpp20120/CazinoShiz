using BotFramework.Host;
using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Leaderboard;

[Command("/top")]
[Command("/balance")]
[Command("/daily")]
[Command("/help")]
public sealed class LeaderboardHandler(
    ILeaderboardService service,
    IDailyBonusService dailyBonus,
    ILocalizer localizer,
    IConfiguration configuration) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Text.StartsWith("/help"))
            await HandleHelpAsync(ctx, msg);
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
        var text = string.Format(Loc("help"), diceDef, dartsDef, footballDef, basketDef, bowlingDef);
        return ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

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

        var r = await dailyBonus.TryClaimAsync(userId, msg.Chat.Id, displayName, ctx.Ct);
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
        var safeName = System.Net.WebUtility.HtmlEncode(user.DisplayName ?? "Unknown").Replace("@", "@\u200B");
        return $"{crown}{safeName} - {user.Coins}";
    }

    private string Loc(string key) => localizer.Get("leaderboard", key);
}
