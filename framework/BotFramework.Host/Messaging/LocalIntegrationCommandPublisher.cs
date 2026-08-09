using BotFramework.Contracts.Messaging;

namespace BotFramework.Host.Messaging;

public sealed class LocalIntegrationCommandPublisher(IServiceProvider services)
    : IIntegrationCommandPublisher
{
    public async Task SendAsync<TCommand>(TCommand command, CancellationToken ct)
        where TCommand : IIntegrationCommand
    {
        var handlers = services.GetServices<IIntegrationCommandHandler<TCommand>>();
        foreach (var handler in handlers)
            await handler.HandleAsync(command, ct);
    }

    public async Task SendAsync(IIntegrationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var handlerType = typeof(IIntegrationCommandHandler<>).MakeGenericType(command.GetType());
        var method = handlerType.GetMethod(nameof(IIntegrationCommandHandler<IIntegrationCommand>.HandleAsync))
            ?? throw new InvalidOperationException($"Integration command handler is missing for '{command.GetType().Name}'.");
        foreach (var handler in services.GetServices(handlerType))
        {
            var task = (Task?)method.Invoke(handler, [command, ct])
                ?? throw new InvalidOperationException($"Integration command handler returned null for '{command.GetType().Name}'.");
            await task;
        }
    }
}
