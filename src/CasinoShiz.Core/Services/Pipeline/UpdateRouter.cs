using System.Reflection;
using CasinoShiz.Services.Handlers;

namespace CasinoShiz.Services.Pipeline;

public sealed partial class UpdateRouter(ILogger<UpdateRouter> logger)
{
    private static readonly IReadOnlyList<Route> Routes = BuildRoutes();

    private readonly record struct Route(RouteAttribute Attribute, Type HandlerType);

    private static IReadOnlyList<Route> BuildRoutes()
    {
        var marker = typeof(IUpdateHandler);
        return marker.Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && marker.IsAssignableFrom(t))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>().Select(a => new Route(a, t)))
            .OrderByDescending(r => r.Attribute.Priority)
            .ToArray();
    }

    public async Task DispatchAsync(UpdateContext ctx)
    {
        foreach (var route in Routes)
        {
            if (!route.Attribute.Matches(ctx.Update)) continue;
            LogRouterMatch(route.Attribute.Name, route.HandlerType.Name);
            var handler = (IUpdateHandler)ctx.Services.GetRequiredService(route.HandlerType);
            await handler.HandleAsync(ctx.Bot, ctx.Update, ctx.Ct);
            return;
        }
        LogRouterMiss(ctx.Update.Id);
    }

    public void LogRegisteredRoutes()
    {
        foreach (var route in Routes)
            LogRouteRegistered(route.Attribute.Priority, route.Attribute.Name, route.HandlerType.Name);
        LogRouteCount(Routes.Count);
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
