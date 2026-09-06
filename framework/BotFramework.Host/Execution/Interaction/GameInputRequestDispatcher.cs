using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameInputRequestDispatcher(
    IGameInputRequestService requests,
    IEnumerable<IGameInputRouteHandler> routeHandlers) : IGameInputRequestDispatcher
{
    private readonly Dictionary<(string GameId, string Route), IGameInputRouteHandler> handlers =
        routeHandlers
            .GroupBy(handler => (handler.GameId, handler.Route))
            .ToDictionary(
                group => group.Key,
                group => group.Count() == 1
                    ? group.Single()
                    : throw new InvalidOperationException(
                        $"Multiple input route handlers are registered for game '{group.Key.GameId}' and route '{group.Key.Route}'."));

    public async Task<GameInputRequestDispatchResult> DispatchAsync(
        GameInputSubmission submission,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var knownRequest = await requests.GetAsync(submission.RequestId, ct);
        if (knownRequest is not null
            && !handlers.ContainsKey((knownRequest.GameId, knownRequest.Route)))
        {
            return new(GameInputRequestDispatchStatus.NoRoute, knownRequest);
        }

        var consumed = await requests.ConsumeAsync(submission, ct);
        if (consumed.Status is not (GameInputRequestConsumeStatus.Accepted or GameInputRequestConsumeStatus.AlreadyConsumed))
            return new(MapStatus(consumed.Status), consumed.Request);

        var request = consumed.Request
            ?? throw new InvalidOperationException("A consumed input request must include its route data.");
        if (!handlers.TryGetValue((request.GameId, request.Route), out var handler))
            return new(GameInputRequestDispatchStatus.NoRoute, request);

        await handler.HandleAsync(request, submission, ct);
        return new(GameInputRequestDispatchStatus.Dispatched, request);
    }

    private static GameInputRequestDispatchStatus MapStatus(GameInputRequestConsumeStatus status) => status switch
    {
        GameInputRequestConsumeStatus.NotFound => GameInputRequestDispatchStatus.NotFound,
        GameInputRequestConsumeStatus.Forbidden => GameInputRequestDispatchStatus.Forbidden,
        GameInputRequestConsumeStatus.Expired => GameInputRequestDispatchStatus.Expired,
        GameInputRequestConsumeStatus.Cancelled => GameInputRequestDispatchStatus.Cancelled,
        GameInputRequestConsumeStatus.InvalidValue => GameInputRequestDispatchStatus.InvalidValue,
        GameInputRequestConsumeStatus.ConsumedByAnotherCorrelation =>
            GameInputRequestDispatchStatus.ConsumedByAnotherCorrelation,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported input request status."),
    };
}
