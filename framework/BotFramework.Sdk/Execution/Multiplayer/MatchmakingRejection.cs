namespace BotFramework.Sdk.Execution;

/// <summary>A machine-readable matchmaking queue rejection.</summary>
public sealed record MatchmakingRejection(MatchmakingRejectionReason Reason)
{
    public string Code => Reason switch
    {
        MatchmakingRejectionReason.PlayerAlreadyQueued => "player_already_queued",
        MatchmakingRejectionReason.PlayerNotQueued => "player_not_queued",
        _ => throw new InvalidOperationException($"Unknown matchmaking rejection '{Reason}'."),
    };
}
