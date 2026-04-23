namespace Games.Darts;

public readonly record struct DartsRollJob(
    long RoundId,
    long ChatId,
    long UserId,
    string DisplayName,
    int ReplyToMessageId);
