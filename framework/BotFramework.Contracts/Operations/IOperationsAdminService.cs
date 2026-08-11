namespace BotFramework.Contracts.Operations;

using BotFramework.Contracts.Games;

public interface IOperationsAdminService
{
    Task<IReadOnlyList<OperationFailure>> ListFailuresAsync(int limit, string? eventType, CancellationToken ct);
    Task<IReadOnlyList<OperationOutbox>> ListOutboxAsync(int limit, string? status, CancellationToken ct);
    Task<IReadOnlyList<OperationJob>> ListJobsAsync(CancellationToken ct);
    Task<IReadOnlyList<OperationAudit>> ListAuditAsync(int limit, string? actor, string? action,
        string? details, DateTimeOffset? from, DateTimeOffset? until, CancellationToken ct);
    Task<WagerWorkflowTimeline?> GetWagerTimelineAsync(string operationId, CancellationToken ct);
    Task<OperationMutationResult> RetryEventAsync(long id, long actorId, string actorName, CancellationToken ct);
    Task<OperationMutationResult> RescheduleOutboxAsync(long id, long actorId, string actorName, CancellationToken ct);
    Task<OperationMutationResult> AdjustWalletAsync(long userId, long balanceScopeId, int delta,
        string operationId, long actorId, string actorName, CancellationToken ct);
    Task<IReadOnlyList<GameAvailability>> ListGameAvailabilityAsync(long? chatId, CancellationToken ct);
    Task<OperationMutationResult> SetGameAvailabilityAsync(long chatId, string gameId, bool enabled,
        string reason, long actorId, string actorName, CancellationToken ct);
    Task<EventReplayReport> ReplayEventStreamAsync(string streamId, long actorId, string actorName, CancellationToken ct);
    Task<EconomySimulationReport> SimulateEconomyAsync(EconomySimulationRequest request,
        long actorId, string actorName, CancellationToken ct);
    Task<IReadOnlyList<FairnessCommitment>> ListIncompleteFairnessAsync(CancellationToken ct);
}
