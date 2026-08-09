using BotFramework.Contracts.Wagering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BotFramework.Rest;

/// <summary>Ingress for workflows that reserve several players as one game.</summary>
public sealed class MultiPartyWagerRestModule : IRestRouteModule
{
    public string ModuleId => "wager-groups";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapRestGroup(ModuleId);
        group.MapPost("", StartAsync).WithName("MultiPartyWagerCreate");
        group.MapGet("/{workflowId}", GetAsync).WithName("MultiPartyWagerGet");
    }

    private static async Task<IResult> StartAsync(
        MultiPartyWagerCreateRequest request,
        RestRequestContext context,
        IMultiPartyWagerCoordinator coordinator,
        CancellationToken ct)
    {
        if (request.Participants is not { Count: >= 2 })
            throw new RestBadRequestException("At least two wager participants are required.");
        if (!request.Participants.Any(x => string.Equals(x.PlayerId, context.Player.Value, StringComparison.Ordinal)))
            throw new RestBadRequestException("The authenticated player must be a participant.");
        var workflowId = context.RequireIdempotencyKey();
        var rulesVersion = request.RulesVersion ?? $"{request.GameId}.v1";
        var result = await coordinator.StartAsync(new MultiPartyWagerRequest(
            workflowId,
            request.GameId,
            request.GameInput,
            request.Participants.Select(x => new MultiPartyWagerParticipant(
                x.OperationId ?? $"{workflowId}:{x.BetId}",
                x.BetId,
                x.PlayerId,
                new WagerTermsSnapshot(rulesVersion, x.Stake, x.Currency, request.SettlementRule))).ToArray(),
            context.Tenant.Value,
            context.Scope.Value,
            DateTimeOffset.UtcNow), ct);
        return Results.Accepted($"/api/v1/tenants/{context.Tenant}/scopes/{context.Scope}/wager-groups/{workflowId}", result);
    }

    private static async Task<IResult> GetAsync(
        string workflowId,
        IMultiPartyWagerCoordinator coordinator,
        CancellationToken ct)
    {
        var result = await coordinator.GetAsync(workflowId, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    public sealed record MultiPartyWagerCreateRequest(
        string GameId,
        string GameInput,
        IReadOnlyList<ParticipantRequest> Participants,
        string? RulesVersion = null,
        string SettlementRule = "standard");

    public sealed record ParticipantRequest(
        string BetId,
        string PlayerId,
        long Stake,
        string Currency,
        string? OperationId = null);
}
