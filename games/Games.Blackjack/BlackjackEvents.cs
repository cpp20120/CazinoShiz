using BotFramework.Sdk;

namespace Games.Blackjack;

public sealed record BlackjackHandCompleted(
    long UserId,
    long ChatId,
    int Bet,
    int Payout,
    int PlayerTotal,
    int DealerTotal,
    string OutcomeName,
    bool Doubled,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.hand_completed";
}
