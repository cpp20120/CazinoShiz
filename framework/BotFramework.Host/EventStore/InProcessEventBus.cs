// ─────────────────────────────────────────────────────────────────────────────
// InProcessEventBus — synchronous, same-process domain event dispatcher.
//
// Subscriptions are matched by pattern at publish time. The pattern grammar
// is intentionally tiny:
//
//   "sh.game_ended"   exact match (module + action)
//   "sh.*"            every event from module "sh"
//   "*.game_ended"    game_ended from any module
//   "*"               everything
//
// Matching happens on publish; subscription is O(patterns). For a small
// framework that's fine. If pattern count ever grows past a hundred, the
// fix is a trie indexed by "module" then "action", not a rewrite.
//
// Subscribers run sequentially in registration order. Each subscriber failure
// is caught and logged — one bad subscriber never kills others or the caller.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed partial class InProcessEventBus(ILogger<InProcessEventBus> logger) : IDomainEventBus
{
    private readonly List<Subscription> _subs = [];

    public void Subscribe(string eventTypePattern, IDomainEventSubscriber subscriber) =>
        _subs.Add(new Subscription(eventTypePattern, subscriber));

    public async Task PublishAsync(IDomainEvent ev, CancellationToken ct)
    {
        foreach (var sub in _subs)
        {
            if (!Matches(sub.Pattern, ev.EventType)) continue;
            try
            {
                await sub.Subscriber.HandleAsync(ev, ct);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(ex, ev.EventType, sub.Subscriber.GetType().Name);
            }
        }
    }

    private static bool Matches(string pattern, string eventType)
    {
        if (pattern == "*") return true;
        if (pattern == eventType) return true;

        var dot = pattern.IndexOf('.');
        if (dot < 0) return false;

        var (patMod, patAction) = (pattern[..dot], pattern[(dot + 1)..]);

        var evDot = eventType.IndexOf('.');
        if (evDot < 0) return false;

        var (evMod, evAction) = (eventType[..evDot], eventType[(evDot + 1)..]);

        return (patMod == "*" || patMod == evMod) && (patAction == "*" || patAction == evAction);
    }

    [LoggerMessage(LogLevel.Error, "event_bus.subscriber_failed event={EventType} subscriber={Subscriber}")]
    partial void LogSubscriberFailed(Exception ex, string eventType, string subscriber);

    private sealed record Subscription(string Pattern, IDomainEventSubscriber Subscriber);
}
