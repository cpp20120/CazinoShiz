namespace BotFramework.Sdk.Execution;

/// <summary>One ordered request to form a match with compatible players.</summary>
public sealed record MatchmakingTicket<TPlayerId, TMatchKey>(
    TPlayerId PlayerId,
    TMatchKey MatchKey,
    DateTimeOffset EnqueuedAt)
    where TPlayerId : notnull
    where TMatchKey : notnull;
