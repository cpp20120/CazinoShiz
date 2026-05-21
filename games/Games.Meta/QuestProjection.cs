using BotFramework.Sdk;

namespace Games.Meta;

public sealed class QuestProjection(
    IQuestService quests,
    IMetaService meta,
    IMetaHistoryStore history,
    ILogger<QuestProjection> logger)
    : DomainEventSubscriber<GameCompletedMetaEvent>
{
    protected override async Task HandleAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        var season = await meta.GetActiveSeasonAsync(ct);
        var updates = await quests.ApplyGameCompletedAsync(ev, ct);
        foreach (var update in updates)
        {
            if (update.Completed)
            {
                await history.AppendAsync(
                    "quest.progressed",
                    "player",
                    $"{season.Id}:{ev.ChatId}:{ev.UserId}",
                    season.Id,
                    ev.ChatId,
                    ev.UserId,
                    new
                    {
                        update.QuestId,
                        update.Progress,
                        update.Target,
                        update.Completed,
                        ev.GameKey,
                        ev.Stake,
                        ev.Payout,
                        ev.IsWin,
                    },
                    ct);

                logger.LogInformation(
                    "Completed quest {QuestId} for user {UserId} in chat {ChatId}: {Progress}/{Target}",
                    update.QuestId,
                    ev.UserId,
                    ev.ChatId,
                    update.Progress,
                    update.Target);
            }
        }
    }
}
