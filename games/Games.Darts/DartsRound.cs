namespace Games.Darts;

public enum DartsRoundStatus : short
{
    Queued = 0,
    AwaitingOutcome = 1,
}

public sealed record DartsRound(
    long Id,
    long UserId,
    long ChatId,
    int Amount,
    DateTimeOffset CreatedAt,
    DartsRoundStatus Status,
    int? BotMessageId,
    int ReplyToMessageId);
