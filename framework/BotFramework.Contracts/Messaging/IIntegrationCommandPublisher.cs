namespace BotFramework.Contracts.Messaging;

public interface IIntegrationCommandPublisher
{
    Task SendAsync<TCommand>(TCommand command, CancellationToken ct)
        where TCommand : IIntegrationCommand;

    /// <summary>
    /// Publishes a command returned by a runtime-selected adapter. The
    /// implementation must dispatch using the concrete command type rather
    /// than the interface type.
    /// </summary>
    Task SendAsync(IIntegrationCommand command, CancellationToken ct);
}
