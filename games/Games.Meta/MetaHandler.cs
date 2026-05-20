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
public sealed class MetaHandler(IMetaService meta) : IUpdateHandler
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
            "Мета-система уже заведена: профиль, ранги и сезонный топ. XP из игр будет подключаться отдельными проекциями."
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

    private static string DisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username)) return "@" + user.Username;
        var name = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(name) ? user.Id.ToString() : name;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm 'UTC'");
}
