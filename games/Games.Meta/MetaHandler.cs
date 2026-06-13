using System.Net;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Games.Meta;

[Command("/season")]
[Command("/profile")]
[Command("/rank")]
[Command("/topseason")]
[Command("/achievements")]
[Command("/streaks")]
[Command("/quests")]
[Command("/quest")]
[Command("/clan")]
public sealed class MetaHandler(IMetaService meta, IQuestService quests, IClanService clans) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        var msg = ctx.Update.Message;
        if (msg?.Text is null) return;

        if (msg.Text.StartsWith("/season", StringComparison.OrdinalIgnoreCase))
            await HandleSeasonAsync(ctx, msg);
        else if (msg.Text.StartsWith("/profile", StringComparison.OrdinalIgnoreCase) ||
                 msg.Text.StartsWith("/rank", StringComparison.OrdinalIgnoreCase))
            await HandleProfileAsync(ctx, msg);
        else if (msg.Text.StartsWith("/topseason", StringComparison.OrdinalIgnoreCase))
            await HandleTopSeasonAsync(ctx, msg);
        else if (msg.Text.StartsWith("/achievements", StringComparison.OrdinalIgnoreCase))
            await HandleAchievementsAsync(ctx, msg);
        else if (msg.Text.StartsWith("/streaks", StringComparison.OrdinalIgnoreCase))
            await HandleStreaksAsync(ctx, msg);
        else if (msg.Text.StartsWith("/quests", StringComparison.OrdinalIgnoreCase))
            await HandleQuestsAsync(ctx, msg);
        else if (msg.Text.StartsWith("/quest", StringComparison.OrdinalIgnoreCase))
            await HandleQuestAsync(ctx, msg);
        else if (msg.Text.StartsWith("/clan", StringComparison.OrdinalIgnoreCase))
            await HandleClanAsync(ctx, msg);
    }

    private async Task HandleSeasonAsync(UpdateContext ctx, Message msg)
    {
        var season = await meta.GetActiveSeasonAsync(ctx.Ct);
        var text = string.Join("\n", [
            "🏁 <b>Текущий сезон</b>",
            $"<b>{Html(season.Name)}</b>",
            $"Статус: <code>{Html(season.Status)}</code>",
            $"Старт: <code>{FormatDate(season.StartsAt)}</code>",
            $"Финиш: <code>{FormatDate(season.EndsAt)}</code>",
            "",
            "Мета-система уже заведена: профиль, ранги, ачивки, квесты, кланы и сезонный топ."
        ]);

        await ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleProfileAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var displayName = DisplayName(user);
        var profile = await meta.GetProfileAsync(msg.Chat.Id, user.Id, displayName, ctx.Ct);
        var player = profile.Player;
        var xpInLevel = Math.Max(0, player.Xp - profile.CurrentLevelXpFloor);
        var xpForNext = Math.Max(1, profile.NextLevelXp - profile.CurrentLevelXpFloor);

        var text = string.Join("\n", [
            "👤 <b>Профиль сезона</b>",
            $"Игрок: <b>{Html(player.DisplayName)}</b>",
            $"Сезон: <b>{Html(profile.Season.Name)}</b>",
            $"Уровень: <b>{player.Level}</b> · XP: <b>{player.Xp}</b>",
            $"Прогресс уровня: <code>{xpInLevel}/{xpForNext}</code>",
            $"Рейтинг: <b>{player.Rating}</b> · Дивизион: <b>{Html(profile.Division)}</b>",
            $"Игры: <b>{player.GamesPlayed}</b> · Победы: <b>{player.Wins}</b> · Поражения: <b>{player.Losses}</b>",
            $"Оборот: ставка <b>{player.TotalStaked}</b> · выплата <b>{player.TotalPayout}</b>"
        ]);

        await ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleTopSeasonAsync(UpdateContext ctx, Message msg)
    {
        var top = await meta.GetTopAsync(msg.Chat.Id, 15, ctx.Ct);
        if (top.Count == 0)
        {
            await ctx.Bot.SendMessage(msg.Chat.Id,
                "🏆 Сезонный топ пока пуст. Напиши /profile, чтобы попасть в текущий сезон.",
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var lines = new List<string> { "🏆 <b>Сезонный топ</b>" };
        foreach (var entry in top)
        {
            lines.Add($"{entry.Place}. <b>{Html(entry.DisplayName)}</b> — XP <b>{entry.Xp}</b>, lvl <b>{entry.Level}</b>, rating <b>{entry.Rating}</b>");
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleAchievementsAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var achievements = await meta.GetAchievementsAsync(msg.Chat.Id, user.Id, ctx.Ct);
        var unlocked = achievements.Count(x => x.IsUnlocked);
        var total = achievements.Count;

        var lines = new List<string> { $"🏆 <b>Ачивки сезона</b> <code>{unlocked}/{total}</code>" };
        foreach (var achievement in achievements)
        {
            var mark = achievement.IsUnlocked ? "✅" : "⬜";
            var suffix = achievement.UnlockedAt is { } at ? $" · <code>{FormatDate(at)}</code>" : "";
            lines.Add($"{mark} <b>{Html(achievement.Title)}</b> — {Html(achievement.Description)}{suffix}");
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleStreaksAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var streaks = await meta.GetGameStreaksAsync(msg.Chat.Id, user.Id, ctx.Ct);
        var lines = new List<string>
        {
            "🔥 <b>Стрики по играм</b>",
            "Серия растёт, если играть каждый день.",
            "",
        };

        foreach (var streak in streaks)
        {
            var lastPlayed = streak.LastPlayedOn is { } day ? $" · <code>{day:dd.MM}</code>" : "";
            lines.Add(
                $"<b>{Html(streak.Title)}</b> {Html(streak.Command)} — сейчас <b>{streak.CurrentStreak}</b>, " +
                $"рекорд <b>{streak.BestStreak}</b>{lastPlayed}");
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleQuestsAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var rows = await quests.GetQuestsAsync(msg.Chat.Id, user.Id, ctx.Ct);
        var lines = new List<string> { "📜 <b>Квесты</b>", "Забрать награду: <code>/quest claim &lt;id&gt;</code>", "" };
        foreach (var q in rows)
        {
            var mark = q.Claimed ? "💰" : q.Completed ? "✅" : "⬜";
            lines.Add($"{mark} <code>{Html(q.Id)}</code> <b>{Html(q.Title)}</b> [{Html(q.Period)}]");
            lines.Add($"   {Html(q.Description)} — <code>{q.Progress}/{q.Target}</code>, reward: <b>{q.RewardXp} XP</b> + <b>{q.RewardCoins}</b> coins");
        }

        await ctx.Bot.SendMessage(msg.Chat.Id, string.Join("\n", lines),
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleQuestAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var parts = msg.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length < 3 || !string.Equals(parts[1], "claim", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Bot.SendMessage(msg.Chat.Id,
                "Использование: <code>/quest claim &lt;id&gt;</code>",
                parseMode: ParseMode.Html,
                replyParameters: new ReplyParameters { MessageId = msg.MessageId },
                cancellationToken: ctx.Ct);
            return;
        }

        var result = await quests.ClaimAsync(msg.Chat.Id, user.Id, DisplayName(user), parts[2], ctx.Ct);
        var text = result switch
        {
            null => "❌ Квест не найден.",
            { Claimed: false } => "⏳ Квест ещё не выполнен или награда уже забрана.",
            _ => $"🎁 Забрана награда за <b>{Html(result.Title)}</b>: <b>{result.RewardXp} XP</b> + <b>{result.RewardCoins}</b> coins"
        };

        await ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task HandleClanAsync(UpdateContext ctx, Message msg)
    {
        var user = msg.From;
        if (user is null) return;

        var parts = msg.Text?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length < 2)
        {
            await ReplyClanHelpAsync(ctx, msg);
            return;
        }

        switch (parts[1].ToLowerInvariant())
        {
            case "create":
                await HandleClanCreateAsync(ctx, msg, user, parts);
                break;
            case "join":
                await HandleClanJoinAsync(ctx, msg, user, parts);
                break;
            case "info":
                await HandleClanInfoAsync(ctx, msg, user, parts);
                break;
            case "members":
                await HandleClanMembersAsync(ctx, msg, user, parts);
                break;
            case "top":
                await HandleClanTopAsync(ctx, msg);
                break;
            default:
                await ReplyClanHelpAsync(ctx, msg);
                break;
        }
    }

    private async Task HandleClanCreateAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 4)
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/clan create &lt;TAG&gt; &lt;name&gt;</code>");
            return;
        }

        var tag = parts[2];
        var name = string.Join(' ', parts.Skip(3));
        var result = await clans.CreateAsync(msg.Chat.Id, user.Id, DisplayName(user), tag, name, ctx.Ct);
        await SendHtmlAsync(ctx, msg, result.Clan is null
            ? $"❌ {Html(result.Message)}"
            : $"✅ {Html(result.Message)} <b>[{Html(result.Clan.Tag)}]</b> {Html(result.Clan.Name)}");
    }

    private async Task HandleClanJoinAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        if (parts.Length < 3)
        {
            await SendHtmlAsync(ctx, msg, "Использование: <code>/clan join &lt;TAG&gt;</code>");
            return;
        }

        var result = await clans.JoinAsync(msg.Chat.Id, user.Id, DisplayName(user), parts[2], ctx.Ct);
        await SendHtmlAsync(ctx, msg, result.Clan is null
            ? $"❌ {Html(result.Message)}"
            : $"✅ {Html(result.Message)} <b>[{Html(result.Clan.Tag)}]</b> {Html(result.Clan.Name)}");
    }

    private async Task HandleClanInfoAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        var clan = parts.Length >= 3
            ? await clans.GetClanByTagAsync(msg.Chat.Id, parts[2], ctx.Ct)
            : await clans.GetUserClanAsync(msg.Chat.Id, user.Id, ctx.Ct);
        if (clan is null)
        {
            await SendHtmlAsync(ctx, msg, "❌ Клан не найден. Создай: <code>/clan create TAG name</code>");
            return;
        }

        await SendHtmlAsync(ctx, msg, string.Join("\n", [
            $"🏰 <b>[{Html(clan.Tag)}] {Html(clan.Name)}</b>",
            $"Участники: <b>{clan.MemberCount}</b>",
            $"Season XP: <b>{clan.SeasonXp}</b>",
            $"Rating: <b>{clan.SeasonRating}</b>",
            $"Создан: <code>{FormatDate(clan.CreatedAt)}</code>",
        ]));
    }

    private async Task HandleClanMembersAsync(UpdateContext ctx, Message msg, User user, string[] parts)
    {
        var clan = parts.Length >= 3
            ? await clans.GetClanByTagAsync(msg.Chat.Id, parts[2], ctx.Ct)
            : await clans.GetUserClanAsync(msg.Chat.Id, user.Id, ctx.Ct);
        if (clan is null)
        {
            await SendHtmlAsync(ctx, msg, "❌ Клан не найден.");
            return;
        }

        var members = await clans.GetMembersAsync(clan.Id, ctx.Ct);
        var lines = new List<string> { $"👥 <b>[{Html(clan.Tag)}] {Html(clan.Name)}</b>" };
        foreach (var member in members.Take(30))
            lines.Add($"• <b>{Html(member.DisplayName)}</b> — <code>{Html(member.Role)}</code>");
        if (members.Count > 30) lines.Add($"…и ещё {members.Count - 30} участников.");
        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private async Task HandleClanTopAsync(UpdateContext ctx, Message msg)
    {
        var top = await clans.GetTopAsync(msg.Chat.Id, 15, ctx.Ct);
        if (top.Count == 0)
        {
            await SendHtmlAsync(ctx, msg, "🏰 Кланов пока нет. Создай: <code>/clan create TAG name</code>");
            return;
        }

        var lines = new List<string> { "🏰 <b>Топ кланов сезона</b>" };
        foreach (var entry in top)
            lines.Add($"{entry.Place}. <b>[{Html(entry.Tag)}] {Html(entry.Name)}</b> — XP <b>{entry.Xp}</b>, rating <b>{entry.Rating}</b>, members <b>{entry.Members}</b>");
        await SendHtmlAsync(ctx, msg, string.Join("\n", lines));
    }

    private Task ReplyClanHelpAsync(UpdateContext ctx, Message msg) => SendHtmlAsync(ctx, msg, string.Join("\n", [
        "🏰 <b>Кланы</b>",
        "<code>/clan create &lt;TAG&gt; &lt;name&gt;</code>",
        "<code>/clan join &lt;TAG&gt;</code>",
        "<code>/clan info [TAG]</code>",
        "<code>/clan members [TAG]</code>",
        "<code>/clan top</code>",
    ]));

    private Task SendHtmlAsync(UpdateContext ctx, Message msg, string text) =>
        ctx.Bot.SendMessage(msg.Chat.Id, text,
            parseMode: ParseMode.Html,
            replyParameters: new ReplyParameters { MessageId = msg.MessageId },
            cancellationToken: ctx.Ct);

    private static string DisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username)) return "@" + user.Username;
        var name = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(name) ? user.Id.ToString() : name;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm 'UTC'");
}
