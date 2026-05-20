using BotFramework.Host;
using Dapper;

namespace Games.Meta;

public interface IQuestStore
{
    Task<IReadOnlyList<QuestProgressUpdate>> ApplyGameCompletedAsync(
        MetaSeason season,
        long chatId,
        long userId,
        GameCompletedMetaEvent ev,
        CancellationToken ct);

    Task<IReadOnlyList<PlayerQuestView>> GetQuestsAsync(
        MetaSeason season,
        long chatId,
        long userId,
        DateTimeOffset now,
        CancellationToken ct);

    Task<QuestClaimResult?> TryMarkClaimedAsync(
        MetaSeason season,
        long chatId,
        long userId,
        string questId,
        DateTimeOffset now,
        CancellationToken ct);
}

public sealed class QuestStore(INpgsqlConnectionFactory connections) : IQuestStore
{
    public async Task<IReadOnlyList<QuestProgressUpdate>> ApplyGameCompletedAsync(
        MetaSeason season,
        long chatId,
        long userId,
        GameCompletedMetaEvent ev,
        CancellationToken ct)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(ev.OccurredAt);
        var updates = new List<QuestProgressUpdate>();

        await using var conn = await connections.OpenAsync(ct);
        foreach (var quest in QuestRegistry.Matching(ev))
        {
            var delta = QuestRegistry.DeltaFor(quest, ev);
            var periodKey = QuestRegistry.PeriodKey(quest, now);

            const string sql = """
                INSERT INTO meta_player_quests (
                    quest_id,
                    season_id,
                    chat_id,
                    user_id,
                    period_key,
                    progress,
                    target,
                    completed
                )
                VALUES (
                    @questId,
                    @seasonId,
                    @chatId,
                    @userId,
                    @periodKey,
                    LEAST(@target, @delta),
                    @target,
                    @delta >= @target
                )
                ON CONFLICT (quest_id, season_id, chat_id, user_id, period_key)
                DO UPDATE SET progress = LEAST(meta_player_quests.target, meta_player_quests.progress + @delta),
                              completed = LEAST(meta_player_quests.target, meta_player_quests.progress + @delta) >= meta_player_quests.target,
                              updated_at = now()
                WHERE meta_player_quests.claimed = false
                RETURNING quest_id AS QuestId,
                          progress,
                          target,
                          completed
                """;

            var row = await conn.QuerySingleOrDefaultAsync<QuestProgressUpdate>(new CommandDefinition(
                sql,
                new
                {
                    questId = quest.Id,
                    seasonId = season.Id,
                    chatId,
                    userId,
                    periodKey,
                    target = quest.Target,
                    delta,
                },
                cancellationToken: ct));

            if (row is not null)
                updates.Add(row);
        }

        return updates;
    }

    public async Task<IReadOnlyList<PlayerQuestView>> GetQuestsAsync(
        MetaSeason season,
        long chatId,
        long userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var periodKeys = QuestRegistry.All
            .Select(q => QuestRegistry.PeriodKey(q, now))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        const string sql = """
            SELECT quest_id AS QuestId,
                   period_key AS PeriodKey,
                   progress,
                   target,
                   completed,
                   claimed
            FROM meta_player_quests
            WHERE season_id = @seasonId
              AND chat_id = @chatId
              AND user_id = @userId
              AND period_key = ANY(@periodKeys)
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<QuestProgressRow>(new CommandDefinition(
            sql,
            new { seasonId = season.Id, chatId, userId, periodKeys },
            cancellationToken: ct));

        var map = rows.ToDictionary(x => (x.QuestId, x.PeriodKey), x => x);
        return QuestRegistry.All.Select(q =>
        {
            var periodKey = QuestRegistry.PeriodKey(q, now);
            map.TryGetValue((q.Id, periodKey), out var row);
            return new PlayerQuestView(
                q.Id,
                q.Title,
                q.Description,
                q.Period,
                row?.Progress ?? 0,
                q.Target,
                row?.Completed ?? false,
                row?.Claimed ?? false,
                q.RewardXp,
                q.RewardCoins);
        }).ToList();
    }

    public async Task<QuestClaimResult?> TryMarkClaimedAsync(
        MetaSeason season,
        long chatId,
        long userId,
        string questId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var quest = QuestRegistry.All.FirstOrDefault(x => string.Equals(x.Id, questId, StringComparison.OrdinalIgnoreCase));
        if (quest is null) return null;

        var periodKey = QuestRegistry.PeriodKey(quest, now);
        const string sql = """
            UPDATE meta_player_quests
            SET claimed = true,
                claimed_at = now(),
                updated_at = now()
            WHERE quest_id = @questId
              AND season_id = @seasonId
              AND chat_id = @chatId
              AND user_id = @userId
              AND period_key = @periodKey
              AND completed = true
              AND claimed = false
            RETURNING quest_id
            """;

        await using var conn = await connections.OpenAsync(ct);
        var claimedQuestId = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            sql,
            new { questId = quest.Id, seasonId = season.Id, chatId, userId, periodKey },
            cancellationToken: ct));

        return claimedQuestId is null
            ? new QuestClaimResult(quest.Id, quest.Title, quest.RewardXp, quest.RewardCoins, false)
            : new QuestClaimResult(quest.Id, quest.Title, quest.RewardXp, quest.RewardCoins, true);
    }

    private sealed record QuestProgressRow(
        string QuestId,
        string PeriodKey,
        int Progress,
        int Target,
        bool Completed,
        bool Claimed);
}
