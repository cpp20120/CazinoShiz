using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Game-owned fact. It deliberately contains no stake, balance or payout.
/// Wagering settles it using the original <see cref="WagerTermsSnapshot"/>.
/// </summary>
public sealed record GameOutcomeDeclared(
    string BetId,
    string GameId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    DateTimeOffset OccurredAt) : IIntegrationEvent, IIntegrationMessageRouted
{
    public string EventType => "game.outcome_declared";
    public string? Topic => $"games.{GameId}.events";
    public string? MessageKey => $"bet:{BetId}";
}
