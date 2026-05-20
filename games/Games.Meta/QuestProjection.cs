using BotFramework.Sdk;

namespace Games.Meta;

public sealed class QuestProjection(IQuestService quests, ILogger<QuestProjection> logger)
    : DomainEventSubscriber<GameCompletedMetaEvent>
{
    protected override async Task HandleAsync(GameCompletedMetaEvent ev, CancellationToken ct)
    {
        var updates = await quests.ApplyGameCompletedAsync(ev, ct);
        foreach (var update in updates.Where(x => x.Completed))
        {
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
