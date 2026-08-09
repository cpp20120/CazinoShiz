using System.Text.Json;
using BotFramework.Contracts.Tenancy;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Workflows;

namespace BotFramework.Host.Wagering;

public sealed class MultiPartyWagerCoordinator(
    IDurableWorkflowDispatcher dispatcher,
    ITenantContextAccessor tenantContext,
    PostgresMultiPartyWagerStore store) : IMultiPartyWagerCoordinator
{
    public Task<MultiPartyWagerResult> StartAsync(
        MultiPartyWagerRequest request,
        CancellationToken ct)
    {
        Validate(request);
        var commandId = $"wager-group:{request.WorkflowId}";
        return dispatcher.DispatchAsync(
            new MultiPartyWagerWorkflowCommand(commandId, request),
            new DurableWorkflowDispatchOptions(
                request.WorkflowId,
                commandId,
                "reserve-play-settle",
                AggregateId: request.WorkflowId),
            () => new MultiPartyWagerResult(request.WorkflowId, "pending", []),
            ct);
    }

    public Task<MultiPartyWagerResult?> GetAsync(string workflowId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId);
        var current = tenantContext.Current
            ?? throw new InvalidOperationException("A tenant context is required to read a multi-party wager.");
        return store.GetResultAsync(workflowId, current.TenantId.Value, current.ScopeId.Value, ct);
    }

    private void Validate(MultiPartyWagerRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameInput);
        using var gameInput = JsonDocument.Parse(request.GameInput);
        if (request.Participants.Count < 2)
            throw new ArgumentException("A multi-party wager requires at least two participants.", nameof(request));
        if (request.Participants.Select(x => x.BetId).Distinct(StringComparer.Ordinal).Count() != request.Participants.Count)
            throw new ArgumentException("Participant BetIds must be unique.", nameof(request));
        if (request.Participants.Any(x => x.Stake <= 0 || string.IsNullOrWhiteSpace(x.PlayerId)))
            throw new ArgumentException("Participant stakes and player ids must be valid.", nameof(request));

        var current = tenantContext.Current;
        if (current is not null
            && (!string.Equals(current.TenantId.Value, request.TenantId, StringComparison.Ordinal)
                || !string.Equals(current.ScopeId.Value, request.ScopeId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Multi-party wager tenant boundary does not match the current request.");
    }
}
