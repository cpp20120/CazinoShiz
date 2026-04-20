// ─────────────────────────────────────────────────────────────────────────────
// CommandBus — dispatcher that runs a command through its middleware chain
// and on to the terminal handler.
//
// Resolution:
//   ICommandHandler<TCommand> comes from DI, one handler per command type.
//   Duplicate registration is a startup-time error; runtime resolution is
//   O(1) because we cache the reflected Handler method per command Type.
//
// Middleware chain is composed once at Host build time (see ctor), not per
// dispatch — that's the usual ASP.NET-Core-middleware pattern and keeps the
// hot path cheap.
//
// Exceptions from the handler bubble back through every middleware in
// reverse order, so logging and metrics middleware observe failures the
// same way they observe successes. No separate error path.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Concurrent;
using System.Reflection;
using BotFramework.Sdk;

namespace BotFramework.Host;

public sealed class CommandBus(
    IServiceProvider services,
    IEnumerable<ICommandMiddleware> middleware) : ICommandBus
{
    private readonly IReadOnlyList<ICommandMiddleware> _middleware = middleware.ToList();
    private readonly ConcurrentDictionary<Type, MethodInfo> _handlerMethods = new();

    public Task SendAsync(ICommand command, CancellationToken ct) =>
        DispatchAsync(command, hasResult: false, ct);

    public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct)
    {
        object? result = null;
        await DispatchAsync(command, hasResult: true, ct, r => result = r);
        return (TResult)result!;
    }

    private Task DispatchAsync(ICommand command, bool hasResult, CancellationToken ct, Action<object?>? capture = null)
    {
        var ctx = new CommandContext(command, RequestContextAccessor.Current, ct);
        var pipeline = BuildPipeline(ctx, hasResult, capture);
        return pipeline();
    }

    private Func<Task> BuildPipeline(CommandContext ctx, bool hasResult, Action<object?>? capture)
    {
        Func<Task> terminal = () => InvokeHandlerAsync(ctx, hasResult, capture);

        for (var i = _middleware.Count - 1; i >= 0; i--)
        {
            var mw = _middleware[i];
            var next = terminal;
            terminal = () => mw.InvokeAsync(ctx, next);
        }

        return terminal;
    }

    private async Task InvokeHandlerAsync(CommandContext ctx, bool hasResult, Action<object?>? capture)
    {
        var commandType = ctx.Command.GetType();
        var handlerInterface = hasResult
            ? typeof(ICommandHandler<,>).MakeGenericType(commandType, commandType.GetInterfaces()
                .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>))
                .GetGenericArguments()[0])
            : typeof(ICommandHandler<>).MakeGenericType(commandType);

        var handler = services.GetService(handlerInterface)
            ?? throw new InvalidOperationException($"No handler registered for {commandType.Name}");

        var method = _handlerMethods.GetOrAdd(commandType, _ => handlerInterface.GetMethod("HandleAsync")!);
        var task = (Task)method.Invoke(handler, [ctx.Command, ctx.Cancellation])!;
        await task;

        if (hasResult && capture is not null)
            capture(task.GetType().GetProperty("Result")!.GetValue(task));
    }
}

/// AsyncLocal shim so middleware can see RequestContext even when framework
/// code creates CommandContext without touching it directly. The Host sets
/// Current per incoming Update / HTTP request.
public static class RequestContextAccessor
{
    private static readonly AsyncLocal<RequestContext?> _current = new();
    public static RequestContext Current
    {
        get => _current.Value ?? Anonymous;
        set => _current.Value = value;
    }
    public static readonly RequestContext Anonymous = new(
        UserId: 0,
        CultureCode: "ru",
        TraceId: "00000000-0000-0000-0000-000000000000",
        Tags: new Dictionary<string, string>());
}
