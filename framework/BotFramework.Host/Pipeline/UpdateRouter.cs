// ─────────────────────────────────────────────────────────────────────────────
// UpdateRouter — dispatches a Telegram Update to the right module handler.
//
// Priority rules live on RouteAttribute (SDK). This class' job is:
//   1. At construction, scan every loaded module's assembly for IUpdateHandler
//      types, collect their RouteAttributes into a single priority-ordered
//      table. Route table is built once per Host lifetime.
//   2. On each dispatch, walk the table top-down; first matching route wins.
//      Handler instance is resolved from DI on every call (handlers are
//      typically Scoped — don't cache instances).
//   3. No match = silently drop, log at Warning. Same behavior as the live
//      bot's router; Telegram retries updates until they're ack'd, so a drop
//      is recoverable.
//
// Why scan module assemblies, not the currently loaded AppDomain:
//   AppDomain scans are slow and pick up every referenced assembly including
//   framework libs — exactly the noise we don't want. Modules are explicit,
//   listed by the Host at composition time; their assemblies are the only
//   place handlers live.
// ─────────────────────────────────────────────────────────────────────────────

using System.Reflection;
using BotFramework.Sdk;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFramework.Host;

public sealed partial class UpdateRouter
{
    private readonly IReadOnlyList<Route> _routes;
    private readonly ILogger<UpdateRouter> _logger;

    private readonly record struct Route(RouteAttribute Attribute, Type HandlerType);

    public UpdateRouter(IEnumerable<IModule> modules, ILogger<UpdateRouter> logger)
    {
        _logger = logger;
        _routes = BuildRoutes(modules);
    }

    private static IReadOnlyList<Route> BuildRoutes(IEnumerable<IModule> modules)
    {
        var marker = typeof(IUpdateHandler);
        var assemblies = modules.Select(m => m.GetType().Assembly).Distinct();

        return assemblies
            .SelectMany(asm => asm.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && marker.IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(a => new Route(a, t)))
            .OrderByDescending(r => r.Attribute.Priority)
            .ToArray();
    }

    public async Task DispatchAsync(ITelegramBotClient bot, Update update, IServiceProvider scopedServices, CancellationToken ct)
    {
        foreach (var route in _routes)
        {
            if (!route.Attribute.Matches(update)) continue;

            LogRouterMatch(route.Attribute.Name, route.HandlerType.Name);
            var handler = (IUpdateHandler)scopedServices.GetRequiredService(route.HandlerType);
            var ctx = new UpdateContext(bot, update, scopedServices, ct);
            await handler.HandleAsync(ctx);
            return;
        }
        LogRouterMiss(update.Id);
    }

    public void LogRegisteredRoutes()
    {
        foreach (var route in _routes)
            LogRouteRegistered(route.Attribute.Priority, route.Attribute.Name, route.HandlerType.Name);
        LogRouteCount(_routes.Count);
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug,
        Message = "router.match route={Route} handler={Handler}")]
    partial void LogRouterMatch(string route, string handler);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "router.miss update_id={UpdateId}")]
    partial void LogRouterMiss(int updateId);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information,
        Message = "router.route priority={Priority} route={Route} handler={Handler}")]
    partial void LogRouteRegistered(int priority, string route, string handler);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "router.registered count={Count}")]
    partial void LogRouteCount(int count);
}
