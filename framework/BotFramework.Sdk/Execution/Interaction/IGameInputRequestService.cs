namespace BotFramework.Sdk.Execution;

/// <summary>
/// Storage and one-time-consumption API used by transport adapters or a generic
/// input dispatcher. All methods are transport-independent.
/// </summary>
public interface IGameInputRequestService
{
    Task<GameInputRequest?> GetAsync(string requestId, CancellationToken ct);

    Task<GameInputRequestConsumeResult> ConsumeAsync(GameInputSubmission submission, CancellationToken ct);
}
