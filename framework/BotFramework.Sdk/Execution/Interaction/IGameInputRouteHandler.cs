namespace BotFramework.Sdk.Execution;

/// <summary>
/// Module-owned bridge from a validated generic input to a strongly typed game
/// command. The implementation should use <see cref="GameInputSubmission.CorrelationId"/>
/// as the command idempotency key.
/// </summary>
public interface IGameInputRouteHandler
{
    string GameId { get; }

    string Route { get; }

    Task HandleAsync(
        GameInputRequest request,
        GameInputSubmission submission,
        CancellationToken ct);
}
