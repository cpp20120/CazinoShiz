using System.Net;
using BotFramework.Host;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Games.Meta;

[Command("/menu")]
[CallbackPrefix("mm:")]
public sealed partial class MetaMenuHandler(
    IMetaService meta,
    IQuestService quests,
    IEconomicsService economics,
    IDailyBonusService dailyBonus,
    ILogger<MetaMenuHandler> logger) : IUpdateHandler
{
    public async Task HandleAsync(UpdateContext ctx)
    {
        if (ctx.Update.CallbackQuery is { } callback)
        {
            await HandleCallbackAsync(ctx, callback);
            return;
        }

        if (ctx.Update.Message is { From: { } user } message)
            await SendHomeAsync(ctx, message.Chat.Id, user, message.MessageId);
    }

    private async Task HandleCallbackAsync(UpdateContext ctx, CallbackQuery callback)
    {
        var parsed = ParseCallback(callback.Data);
        if (parsed is null)
        {
            await AnswerAsync(ctx, callback, "Кнопка устарела. Открой /menu заново.", alert: true);
            return;
        }

        var (ownerId, action, argument) = parsed.Value;
        if (callback.From.Id != ownerId)
        {
            await AnswerAsync(ctx, callback, "Это меню другого игрока.", alert: true);
            return;
        }

        if (callback.Message is not { } message)
        {
            await AnswerAsync(ctx, callback);
            return;
        }

        var displayName = DisplayName(callback.From);
        string? toast = null;

        try
        {
            switch (action)
            {
                case "home":
                    await EditHomeAsync(ctx, message, callback.From);
                    break;
                case "profile":
                    await EditProfileAsync(ctx, message, callback.From);
                    break;
                case "quests":
                    await EditQuestsAsync(ctx, message, callback.From);
                    break;
                case "achievements":
                    await EditAchievementsAsync(ctx, message, callback.From);
                    break;
                case "streaks":
                    await EditStreaksAsync(ctx, message, callback.From);
                    break;
                case "top":
                    await EditTopAsync(ctx, message, ownerId);
                    break;
                case "games":
                    await EditAsync(ctx, message, GamesText(), GamesMarkup(ownerId));
                    break;
                case "daily":
                    toast = DailyToast(await dailyBonus.TryClaimAsync(
                        ownerId, message.Chat.Id, displayName, ctx.Ct));
                    await EditHomeAsync(ctx, message, callback.From);
                    break;
                case "claim":
                    toast = await ClaimQuestAsync(message.Chat.Id, ownerId, displayName, argument, ctx.Ct);
                    await EditQuestsAsync(ctx, message, callback.From);
                    break;
                case "claimall":
                    toast = await ClaimAllAsync(message.Chat.Id, ownerId, displayName, ctx.Ct);
                    await EditQuestsAsync(ctx, message, callback.From);
                    break;
                case "close":
                    await EditAsync(ctx, message, "Меню закрыто.", null);
                    break;
                default:
                    toast = "Неизвестное действие.";
                    break;
            }
        }
        catch (Exception ex)
        {
            LogMenuActionFailed(action, ownerId, message.Chat.Id, ex);
            toast = "Не удалось обновить меню. Попробуй ещё раз.";
        }

        await AnswerAsync(ctx, callback, toast, alert: toast?.StartsWith("Не удалось") == true);
    }

    private async Task SendHomeAsync(UpdateContext ctx, long chatId, User user, int replyToMessageId)
    {
        var (text, markup) = await BuildHomeAsync(chatId, user, ctx.Ct);
        await ctx.Bot.SendMessage(
            chatId,
            text,
            parseMode: ParseMode.Html,
            replyMarkup: markup,
            replyParameters: new ReplyParameters { MessageId = replyToMessageId },
            cancellationToken: ctx.Ct);
    }

    private async Task EditHomeAsync(UpdateContext ctx, Message message, User user)
    {
        var (text, markup) = await BuildHomeAsync(message.Chat.Id, user, ctx.Ct);
        await EditAsync(ctx, message, text, markup);
    }

    private async Task<(string Text, InlineKeyboardMarkup Markup)> BuildHomeAsync(
        long chatId,
        User user,
        CancellationToken ct)
    {
        var displayName = DisplayName(user);
        await economics.EnsureUserAsync(user.Id, chatId, displayName, ct);

        var balanceTask = economics.GetBalanceAsync(user.Id, chatId, ct);
        var profileTask = meta.GetProfileAsync(chatId, user.Id, displayName, ct);
        var questsTask = quests.GetQuestsAsync(chatId, user.Id, ct);
        var achievementsTask = meta.GetAchievementsAsync(chatId, user.Id, ct);
        await Task.WhenAll(balanceTask, profileTask, questsTask, achievementsTask);

        var profile = await profileTask;
        var questRows = await questsTask;
        var achievementRows = await achievementsTask;
        var completedQuests = questRows.Count(x => x.Completed && !x.Claimed);
        var unlocked = achievementRows.Count(x => x.IsUnlocked);

        var text = string.Join("\n", [
            "🎰 <b>CasinoShiz</b>",
            $"Игрок: <b>{Html(displayName)}</b>",
            "",
            $"💰 Баланс: <b>{await balanceTask}</b>",
            $"⭐ Уровень: <b>{profile.Player.Level}</b> · XP: <b>{profile.Player.Xp}</b>",
            $"🏅 Ранг: <b>{Html(profile.Division)}</b> · рейтинг <b>{profile.Player.Rating}</b>",
            $"📜 Квесты: <b>{completedQuests}</b> наград ждут",
            $"🏆 Ачивки: <b>{unlocked}/{achievementRows.Count}</b>",
            "",
            $"Сезон: <b>{Html(profile.Season.Name)}</b> · до <code>{profile.Season.EndsAt:dd.MM.yyyy}</code>",
        ]);

        return (text, HomeMarkup(user.Id, completedQuests > 0));
    }

    private async Task EditProfileAsync(UpdateContext ctx, Message message, User user)
    {
        var profile = await meta.GetProfileAsync(message.Chat.Id, user.Id, DisplayName(user), ctx.Ct);
        var player = profile.Player;
        var xpInLevel = Math.Max(0, player.Xp - profile.CurrentLevelXpFloor);
        var xpForNext = Math.Max(1, profile.NextLevelXp - profile.CurrentLevelXpFloor);
        var winsPercent = player.GamesPlayed == 0
            ? 0
            : (int)Math.Round(player.Wins * 100d / player.GamesPlayed);

        var text = string.Join("\n", [
            "👤 <b>Профиль сезона</b>",
            $"Игрок: <b>{Html(player.DisplayName)}</b>",
            $"Дивизион: <b>{Html(profile.Division)}</b>",
            $"Уровень: <b>{player.Level}</b> · XP <b>{player.Xp}</b>",
            $"До следующего уровня: <code>{xpInLevel}/{xpForNext}</code>",
            $"Рейтинг: <b>{player.Rating}</b>",
            "",
            $"Игры: <b>{player.GamesPlayed}</b>",
            $"Победы: <b>{player.Wins}</b> · поражения: <b>{player.Losses}</b>",
            $"Win rate: <b>{winsPercent}%</b>",
            $"Поставлено: <b>{player.TotalStaked}</b> · получено: <b>{player.TotalPayout}</b>",
        ]);

        await EditAsync(ctx, message, text, BackMarkup(user.Id));
    }

    private async Task EditQuestsAsync(UpdateContext ctx, Message message, User user)
    {
        var rows = await quests.GetQuestsAsync(message.Chat.Id, user.Id, ctx.Ct);
        var lines = new List<string> { "📜 <b>Квесты</b>" };

        foreach (var quest in rows)
        {
            var mark = quest.Claimed ? "💰" : quest.Completed ? "✅" : "⬜";
            lines.Add(
                $"{mark} <b>{Html(quest.Title)}</b> · <code>{quest.Progress}/{quest.Target}</code>\n" +
                $"   {Html(quest.Description)} · <b>{quest.RewardXp} XP</b> + <b>{quest.RewardCoins}</b> монет");
        }

        if (rows.Count == 0)
            lines.Add("Активных квестов пока нет.");

        await EditAsync(ctx, message, string.Join("\n", lines), QuestsMarkup(user.Id, rows));
    }

    private async Task EditAchievementsAsync(UpdateContext ctx, Message message, User user)
    {
        var rows = await meta.GetAchievementsAsync(message.Chat.Id, user.Id, ctx.Ct);
        var unlocked = rows.Count(x => x.IsUnlocked);
        var lines = new List<string> { $"🏆 <b>Ачивки</b> · <code>{unlocked}/{rows.Count}</code>" };

        foreach (var achievement in rows.OrderByDescending(x => x.IsUnlocked))
        {
            var mark = achievement.IsUnlocked ? "✅" : "⬜";
            lines.Add($"{mark} <b>{Html(achievement.Title)}</b> — {Html(achievement.Description)}");
        }

        await EditAsync(ctx, message, string.Join("\n", lines), BackMarkup(user.Id));
    }

    private async Task EditStreaksAsync(UpdateContext ctx, Message message, User user)
    {
        var rows = await meta.GetGameStreaksAsync(message.Chat.Id, user.Id, ctx.Ct);
        var lines = new List<string>
        {
            "🔥 <b>Стрики по играм</b>",
            "Играй каждый день, чтобы продолжать серию.",
        };

        foreach (var row in rows)
            lines.Add($"<b>{Html(row.Title)}</b> — сейчас <b>{row.CurrentStreak}</b> · рекорд <b>{row.BestStreak}</b>");

        await EditAsync(ctx, message, string.Join("\n", lines), BackMarkup(user.Id));
    }

    private async Task EditTopAsync(UpdateContext ctx, Message message, long ownerId)
    {
        var rows = await meta.GetTopAsync(message.Chat.Id, 15, ctx.Ct);
        var lines = new List<string> { "🏆 <b>Сезонный топ</b>" };

        foreach (var row in rows)
            lines.Add($"{row.Place}. <b>{Html(row.DisplayName)}</b> · lvl {row.Level} · XP {row.Xp} · rating {row.Rating}");

        if (rows.Count == 0)
            lines.Add("Топ пока пуст.");

        await EditAsync(ctx, message, string.Join("\n", lines), BackMarkup(ownerId));
    }

    private async Task<string> ClaimQuestAsync(
        long chatId,
        long userId,
        string displayName,
        string? questId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(questId))
            return "Квест не найден.";

        var result = await quests.ClaimAsync(chatId, userId, displayName, questId, ct);
        return result switch
        {
            null => "Квест не найден.",
            { Claimed: false } => "Награда ещё недоступна или уже забрана.",
            _ => $"+{result.RewardXp} XP и +{result.RewardCoins} монет",
        };
    }

    private async Task<string> ClaimAllAsync(
        long chatId,
        long userId,
        string displayName,
        CancellationToken ct)
    {
        var rows = await quests.GetQuestsAsync(chatId, userId, ct);
        long xp = 0;
        long coins = 0;
        var claimed = 0;

        foreach (var quest in rows.Where(x => x.Completed && !x.Claimed))
        {
            var result = await quests.ClaimAsync(chatId, userId, displayName, quest.Id, ct);
            if (result is not { Claimed: true }) continue;
            claimed++;
            xp += result.RewardXp;
            coins += result.RewardCoins;
        }

        return claimed == 0
            ? "Нет доступных наград."
            : $"Забрано: {claimed} · +{xp} XP · +{coins} монет";
    }

    private async Task EditAsync(
        UpdateContext ctx,
        Message message,
        string text,
        InlineKeyboardMarkup? markup)
    {
        try
        {
            await ctx.Bot.EditMessageText(
                message.Chat.Id,
                message.MessageId,
                text,
                parseMode: ParseMode.Html,
                replyMarkup: markup,
                cancellationToken: ctx.Ct);
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
        {
        }
    }

    private static InlineKeyboardMarkup HomeMarkup(long ownerId, bool hasQuestRewards)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                Button("👤 Профиль", ownerId, "profile"),
                Button("📜 Квесты", ownerId, "quests")
            },
            new[]
            {
                Button("🏆 Ачивки", ownerId, "achievements"),
                Button("🔥 Стрики", ownerId, "streaks")
            },
            new[]
            {
                Button("🥇 Топ сезона", ownerId, "top"),
                Button("🎁 Ежедневный бонус", ownerId, "daily")
            },
            new[]
            {
                Button("🎮 Игры", ownerId, "games")
            }
        };

        if (hasQuestRewards)
            rows.Insert(1, new[] { Button("💰 Забрать награды за квесты", ownerId, "claimall") });

        rows.Add(new[] { Button("✖ Закрыть", ownerId, "close") });
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup QuestsMarkup(long ownerId, IReadOnlyList<PlayerQuestView> quests)
    {
        var rows = quests
            .Where(x => x.Completed && !x.Claimed)
            .Take(8)
            .Select(x => new[] { Button($"💰 Забрать: {x.Title}", ownerId, "claim", x.Id) })
            .ToList();

        if (rows.Count > 1)
            rows.Insert(0, new[] { Button("💰 Забрать всё", ownerId, "claimall") });

        rows.Add(new[] { Button("← Меню", ownerId, "home") });
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup GamesMarkup(long ownerId) => new([
        [Button("↻ Обновить", ownerId, "games")],
        [Button("← Меню", ownerId, "home")],
    ]);

    private static InlineKeyboardMarkup BackMarkup(long ownerId) => new([
        [Button("← Меню", ownerId, "home")],
    ]);

    private static InlineKeyboardButton Button(string text, long ownerId, string action, string? argument = null)
    {
        var data = argument is null
            ? $"mm:{ownerId}:{action}"
            : $"mm:{ownerId}:{action}:{argument}";
        return InlineKeyboardButton.WithCallbackData(text, data);
    }

    private static string GamesText() => string.Join("\n", [
        "🎮 <b>Игры</b>",
        "",
        "<b>Быстрые ставки</b>",
        "🎲 /dice · 🎯 /darts · ⚽ /football",
        "🏀 /basket · 🎳 /bowling · 🎰 /slots",
        "",
        "<b>Карточные и групповые</b>",
        "🃏 /blackjack · ♠️ /poker · 🎩 /sh",
        "🐎 /horse · ⚔️ /challenge",
        "",
        "<b>Прочее</b>",
        "🎯 /pick · 🎟 /picklottery · 🖼 /pixelbattle",
        "",
        "<i>Нажми на команду в сообщении, чтобы вставить её в поле ввода.</i>",
    ]);

    private static (long OwnerId, string Action, string? Argument)? ParseCallback(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return null;
        var parts = data.Split(':', 4, StringSplitOptions.None);
        if (parts.Length < 3 || parts[0] != "mm" || !long.TryParse(parts[1], out var ownerId))
            return null;
        return (ownerId, parts[2], parts.Length == 4 ? parts[3] : null);
    }

    private static string DailyToast(DailyBonusClaimResult result) => result.Status switch
    {
        DailyBonusClaimStatus.Claimed => $"+{result.BonusCoins} монет · баланс {result.NewBalance}",
        DailyBonusClaimStatus.AlreadyClaimedToday => "Сегодня бонус уже получен.",
        DailyBonusClaimStatus.Disabled => "Ежедневный бонус выключен.",
        DailyBonusClaimStatus.IneligibleEmptyBalance => "Для бонуса нужен положительный баланс.",
        DailyBonusClaimStatus.IneligiblePercentRoundsToZero => "Баланс пока слишком мал для бонуса.",
        _ => "Бонус сейчас недоступен.",
    };

    private static Task AnswerAsync(
        UpdateContext ctx,
        CallbackQuery callback,
        string? text = null,
        bool alert = false) =>
        ctx.Bot.AnswerCallbackQuery(
            callback.Id,
            text,
            showAlert: alert,
            cancellationToken: ctx.Ct);

    private static string DisplayName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username)) return "@" + user.Username;
        var name = string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(name) ? user.Id.ToString() : name;
    }

    private static string Html(string value) => WebUtility.HtmlEncode(value);

    [LoggerMessage(
        EventId = 7401,
        Level = LogLevel.Error,
        Message = "meta.menu action failed action={Action} user={UserId} chat={ChatId}")]
    partial void LogMenuActionFailed(string action, long userId, long chatId, Exception exception);
}
