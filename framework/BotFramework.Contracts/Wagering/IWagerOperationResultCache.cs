namespace BotFramework.Contracts.Wagering;

/// <summary>Optional fast read projection for eventual operation results.</summary>
public interface IWagerOperationResultCache
{
    Task<WagerOperation?> GetAsync(string operationId, CancellationToken ct);
    Task SetAsync(WagerOperation operation, CancellationToken ct);
}
