using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Starts an asynchronous wager saga. <see cref="GameInput"/> is opaque to
/// Wagering and is interpreted only by the addressed game service.
/// </summary>
public sealed record WagerRequested(
    string OperationId,
    string BetId,
    string GameId,
    string PlayerId,
    string GameInput,
    WagerTermsSnapshot Terms,
    DateTimeOffset OccurredAt) : IIntegrationCommand, IIntegrationMessageRouted
{
    public string CommandType => "wager.requested";
    public string? Topic => $"wagering.{GameId}.commands";
    public string? MessageKey => $"wager:{PlayerId}";
}
