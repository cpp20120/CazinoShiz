namespace BotFramework.Contracts.Wagering;

/// <summary>Durable idempotency and status store for eventual wager results.</summary>
public interface IWagerOperationStore
{
    Task<WagerOperation> CreateOrGetAsync(WagerOperation operation, CancellationToken ct);
    Task<WagerOperation?> GetAsync(string operationId, CancellationToken ct);
    Task<WagerOperation?> GetByBetIdAsync(string betId, CancellationToken ct);
    Task<WagerOperation> TransitionAsync(string operationId, WagerOperationStatus status, string? outcomeCode, string? errorCode, CancellationToken ct);
    Task<WagerOperation?> TryTransitionAsync(
        string operationId,
        WagerOperationStatus expectedStatus,
        WagerOperationStatus status,
        string? outcomeCode,
        string? errorCode,
        CancellationToken ct);
}
