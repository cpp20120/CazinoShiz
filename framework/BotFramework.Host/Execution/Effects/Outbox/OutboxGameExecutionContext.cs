namespace BotFramework.Host.Execution;

internal sealed class OutboxGameExecutionContext(GameEffectOutboxItem item) : IGameExecutionContext
{
    public string? OperationId => item.OperationId;

    public string? GameId => item.GameId;

    public string? AggregateId => item.AggregateId;

    public string? EffectDeliveryId => $"game-effect:{item.Id}";

    public BotFramework.Contracts.Tenancy.TenantContext? TenantContext => item.TenantContext;

    public Task<int> ExecuteAsync(string sql, object? parameters, CancellationToken ct) =>
        throw new InvalidOperationException(
            "Durable effect delivery runs after the game transaction and cannot execute transaction-bound SQL.");

    public Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? parameters, CancellationToken ct) =>
        throw new InvalidOperationException(
            "Durable effect delivery runs after the game transaction and cannot execute transaction-bound SQL.");
}
