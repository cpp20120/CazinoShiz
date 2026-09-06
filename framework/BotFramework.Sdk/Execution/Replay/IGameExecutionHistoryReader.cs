namespace BotFramework.Sdk.Execution;

/// <summary>
/// Read-only port for the journal emitted by atomic and state-only game
/// executors. Implementations must enforce the caller's normal tenant/admin
/// boundary before returning raw command or private-state payloads.
/// </summary>
public interface IGameExecutionHistoryReader
{
    Task<GameExecutionHistoryEntry?> GetAsync(string commandId, CancellationToken ct = default);

    Task<IReadOnlyList<GameExecutionHistoryEntry>> ListAsync(
        string gameId,
        string aggregateId,
        int take = 100,
        CancellationToken ct = default);
}
