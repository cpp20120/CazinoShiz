using BotFramework.Sdk.Execution;
using BotFramework.Contracts.Tenancy;

namespace BotFramework.Host.Execution;

public interface IGameExecutionContext
{
    string? OperationId => null;
    string? GameId => null;
    string? AggregateId => null;
    string? EffectDeliveryId => null;
    TenantContext? TenantContext => null;

    /// <summary>
    /// Applies a wallet batch using the deployment's wallet boundary. In the
    /// monolith this stays inside the current game transaction; in
    /// microservices mode the implementation delegates to Wallet over gRPC.
    /// </summary>
    Task<bool> ApplyWalletAsync(
        long userId,
        long balanceScopeId,
        IReadOnlyList<EconomyEffect> effects,
        string operationId,
        CancellationToken ct) =>
        throw new InvalidOperationException("This execution context does not provide wallet mutations.");

    Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct);

    Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken ct);
}
