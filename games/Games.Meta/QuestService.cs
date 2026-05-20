using BotFramework.Host.Services;
using BotFramework.Sdk;

namespace Games.Meta;

public interface IQuestService
{
    Task<IReadOnlyList<QuestProgressUpdate>> ApplyGameCompletedAsync(GameCompletedMetaEvent ev, CancellationToken ct);
    Task<IReadOnlyList<PlayerQuestView>> GetQuestsAsync(long chatId, long userId, CancellationToken ct);
    Task<QuestClaimResult?> ClaimAsync(long chatId, long userId, string displayName, string questId, CancellationToken ct);
}

public sealed class QuestService(
    IMetaService meta,
    IQuestStore quests,
    IEconomicsService economics) : IQuestService
{
    public async Task<IReadOnlyList<QuestProgressUpdate>> ApplyGameCompletedAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await quests.ApplyGameCompletedAsync(season, ev.ChatId, ev.UserId, ev, ct);
    }

    public async Task<IReadOnlyList<PlayerQuestView>> GetQuestsAsync(long chatId, long userId, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        return await quests.GetQuestsAsync(season, chatId, userId, DateTimeOffset.UtcNow, ct);
    }

    public async Task<QuestClaimResult?> ClaimAsync(long chatId, long userId, string displayName, string questId, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var result = await quests.TryMarkClaimedAsync(season, chatId, userId, questId, DateTimeOffset.UtcNow, ct);
        if (result is not { Claimed: true }) return result;

        await economics.EnsureUserAsync(userId, chatId, displayName, ct);
        if (result.RewardCoins > 0)
        {
            var amount = (int)Math.Min(int.MaxValue, result.RewardCoins);
            await economics.CreditOnceAsync(
                userId,
                chatId,
                amount,
                "quest.reward",
                $"quest:reward:{season.Id}:{chatId}:{userId}:{result.QuestId}",
                ct);
        }

        if (result.RewardXp > 0)
        {
            await meta.AddSeasonXpAsync(
                season.Id,
                chatId,
                userId,
                displayName,
                result.RewardXp,
                ct);
        }

        return result;
    }
}
