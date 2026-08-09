using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BotFramework.Rest;

/// <summary>Generic eventual wager API. Game modules may expose richer aliases over it.</summary>
public sealed class WagerRestModule : IRestRouteModule
{
    public string ModuleId => "wagers";

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapRestGroup(ModuleId);
        group.MapPost("", CreateAsync).WithName("WagerCreate");
        group.MapGet("/{operationId}", GetAsync).WithName("WagerOperation");
    }

    private static async Task<IResult> CreateAsync(
        WagerCreateRequest request,
        RestRequestContext context,
        IWagerOperationStore operations,
        IIntegrationCommandPublisher commands,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GameId) || string.IsNullOrWhiteSpace(request.BetId))
            throw new RestBadRequestException("gameId and betId are required.");
        if (request.Terms.Stake <= 0 || string.IsNullOrWhiteSpace(request.Terms.Currency))
            throw new RestBadRequestException("terms.stake must be positive and terms.currency is required.");
        var operationId = context.RequireIdempotencyKey();
        var now = DateTimeOffset.UtcNow;
        var operation = await operations.CreateOrGetAsync(new WagerOperation(
            operationId, request.BetId, request.GameId, context.Player.Value,
            request.GameInput, System.Text.Json.JsonSerializer.Serialize(request.Terms),
            WagerOperationStatus.Pending, null, null, now, now), ct);
        if (operation.Status == WagerOperationStatus.Pending)
            await commands.SendAsync(new WagerRequested(operationId, request.BetId, request.GameId,
                context.Player.Value, request.GameInput, request.Terms, now), ct);
        return Results.Accepted($"/api/v1/tenants/{context.Tenant}/scopes/{context.Scope}/wagers/{operationId}", operation);
    }

    private static async Task<IResult> GetAsync(string operationId, IWagerOperationStore operations, IWagerOperationResultCache cache, CancellationToken ct)
    {
        var operation = await cache.GetAsync(operationId, ct) ?? await operations.GetAsync(operationId, ct);
        return operation is null ? Results.NotFound() : Results.Ok(operation);
    }

    public sealed record WagerCreateRequest(string GameId, string BetId, string GameInput, WagerTermsSnapshot Terms);
}
