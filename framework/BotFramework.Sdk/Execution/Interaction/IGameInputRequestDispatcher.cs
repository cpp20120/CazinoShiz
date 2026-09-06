namespace BotFramework.Sdk.Execution;

/// <summary>Consumes one request and invokes the matching module route handler.</summary>
public interface IGameInputRequestDispatcher
{
    Task<GameInputRequestDispatchResult> DispatchAsync(GameInputSubmission submission, CancellationToken ct);
}
