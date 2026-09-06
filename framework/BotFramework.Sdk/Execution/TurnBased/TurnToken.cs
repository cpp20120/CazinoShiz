namespace BotFramework.Sdk.Execution;

/// <summary>
/// Identifies one exact turn. Persist this token in an input request or timeout
/// command so a delayed delivery cannot affect a later turn of the same player.
/// </summary>
public sealed record TurnToken<TPlayerId>
    where TPlayerId : notnull
{
    public TurnToken(long number, TPlayerId playerId)
    {
        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number), "Turn number must be positive.");

        Number = number;
        PlayerId = playerId;
    }

    public long Number { get; }

    public TPlayerId PlayerId { get; }
}
