using Dapper;
using BotFramework.Contracts.Tenancy;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameExecutionContext(
    IGameExecutionSession session,
    IAtomicEconomics? economics = null,
    string? operationId = null,
    TenantContext? tenantContext = null,
    string? gameId = null,
    string? aggregateId = null) : IGameExecutionContext
{
    public string? OperationId { get; } = operationId;
    public string? GameId { get; } = gameId;
    public string? AggregateId { get; } = aggregateId;
    public TenantContext? TenantContext { get; } = tenantContext;

    public async Task<bool> ApplyWalletAsync(
        long userId,
        long balanceScopeId,
    IReadOnlyList<EconomyEffect> effects,
    string operationId,
        CancellationToken ct)
    {
        var result = await (economics ?? throw new InvalidOperationException(
                "Wallet mutations are unavailable for this execution context.")).ApplyAsync(
            new WalletIdentity(userId, balanceScopeId),
            effects,
            session,
            operationId,
            ct);
        return !result.Rejected;
    }

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: ct));

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
            sql,
            parameters,
            session.Transaction,
            cancellationToken: ct));
}
