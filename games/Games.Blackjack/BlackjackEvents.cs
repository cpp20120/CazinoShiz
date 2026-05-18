using BotFramework.Sdk;

namespace Games.Blackjack;

public sealed record BlackjackHandStarted(
    long UserId,
    long ChatId,
    int Bet,
    string PlayerCards,
    string DealerCards,
    string DeckState,
    int? StateMessageId,
    long CreatedAtMs,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.hand_started";
}

public sealed record BlackjackHandUpdated(
    long UserId,
    long ChatId,
    int Bet,
    string PlayerCards,
    string DealerCards,
    string DeckState,
    int? StateMessageId,
    long CreatedAtMs,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.hand_updated";
}

public sealed record BlackjackStateMessageSet(
    long UserId,
    int StateMessageId,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.state_message_set";
}

public sealed record BlackjackHandClosed(
    long UserId,
    long ChatId,
    long CreatedAtMs,
    string Reason,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "blackjack.hand_closed";
}

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