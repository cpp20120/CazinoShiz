using BotFramework.Sdk;

namespace BotFramework.Host.Services;

internal sealed class ClickHouseEventMirror(ClickHouseAnalyticsService analytics) : IDomainEventSubscriber
{
    public Task HandleAsync(IDomainEvent ev, CancellationToken ct)
    {
        analytics.TrackDomainEvent(ev);
        return Task.CompletedTask;
    }
}
