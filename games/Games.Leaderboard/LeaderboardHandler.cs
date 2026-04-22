using BotFramework.Host;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Leaderboard;

[Command("/top")]
[Command("/balance")]
[Command("/help")]
[Command("/__debug")]
public sealed class LeaderboardHandler(
    ILeaderboardService service,
    ILocalizer localizer) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text == null) return;

        if (msg.Text.StartsWith("/__debug"))
            await HandleDebugAsync(ctx, msg);
        else if (msg.Text.StartsWith("/help"))
            await HandleHelpAsync(ctx, msg);
        else if (msg.Text.StartsWith("/top"))
            await HandleTopAsync(ctx, msg);
        else if (msg.Text.StartsWith("/balance"))
            await HandleBalanceAsync(ctx, msg);
    }

    private Task HandleDebugAsync(UpdateContext ctx, Message msg) =>
        ctx.Bot.SendMessage(msg.Chat.Id,
            $"userId : {msg.From?.Id}\nchatId : {msg.Chat.Id}",
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);

    private Task HandleHelpAsync(UpdateContext ctx, Message msg) =>
        ctx.Bot.SendMessage(msg.Chat.Id, Loc("help"),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);

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

    private static string FormatUser(LeaderboardUser user, bool isFirstPlace)
    {
        var crown = isFirstPlace ? "👑 " : "";
        return $"{crown}{user.DisplayName} - {user.Coins}";
    }

    private string Loc(string key) => localizer.Get("leaderboard", key);
}
