using BotFramework.Sdk.Events.Contracts;

namespace BotFramework.Host.Wagering;

/// <summary>
/// Internal game fact. GameEventOutboxDispatcher relays it to the integration
/// outbox only after the game state transaction has committed.
/// </summary>
public sealed record WagerGameOutcomeDeclared(
    string GameId,
    string BetId,
    string PlayerId,
    string OutcomeCode,
    string Evidence,
    long Revision,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "wager.game.outcome_declared";
}
