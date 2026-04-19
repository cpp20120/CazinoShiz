// ─────────────────────────────────────────────────────────────────────────────
// EventSubscriptionInitializer — hydrates InProcessEventBus at app startup.
//
// Modules declare subscriptions through IModuleServiceCollection
// .AddDomainEventSubscription<TSubscriber>(pattern); that call records the
// (pattern, subscriber type) pair in ModuleRegistrations.EventSubscriptions.
// Here we resolve each subscriber as a singleton and attach it to the bus
// before any handler runs.
//
// Registration order matches ModuleLoader iteration order, which matches the
// order AddModule<T>() was called in the builder. Same order a reader of
// Program.cs sees.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed partial class EventSubscriptionInitializer(
    IDomainEventBus bus,
    ModuleRegistrations registrations,
    IServiceProvider services,
    ILogger<EventSubscriptionInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        bus.Subscribe("*", services.GetRequiredService<EventLogSubscriber>());
        LogSubscriptionRegistered("*", nameof(EventLogSubscriber));

        bus.Subscribe("*", services.GetRequiredService<Services.ClickHouseEventMirror>());
        LogSubscriptionRegistered("*", nameof(Services.ClickHouseEventMirror));

        foreach (var sub in registrations.EventSubscriptions)
        {
            var subscriber = (IDomainEventSubscriber)services.GetRequiredService(sub.SubscriberType);
            bus.Subscribe(sub.EventTypePattern, subscriber);
            LogSubscriptionRegistered(sub.EventTypePattern, sub.SubscriberType.Name);
        }
        LogSubscriptionCount(registrations.EventSubscriptions.Count);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "eventbus.subscription pattern={Pattern} subscriber={Subscriber}")]
    partial void LogSubscriptionRegistered(string pattern, string subscriber);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "eventbus.registered count={Count}")]
    partial void LogSubscriptionCount(int count);
}
