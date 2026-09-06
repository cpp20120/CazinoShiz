namespace BotFramework.Sdk.Execution;

public sealed record TurnEngineRejection(TurnEngineRejectionReason Reason)
{
    public string Code => Reason switch
    {
        TurnEngineRejectionReason.GameNotWaiting => "game_not_waiting",
        TurnEngineRejectionReason.GameNotActive => "game_not_active",
        TurnEngineRejectionReason.GameNotSuspended => "game_not_suspended",
        TurnEngineRejectionReason.GameIsTerminal => "game_is_terminal",
        TurnEngineRejectionReason.NoPlayers => "no_players",
        TurnEngineRejectionReason.PlayerAlreadyJoined => "player_already_joined",
        TurnEngineRejectionReason.PlayerNotFound => "player_not_found",
        TurnEngineRejectionReason.StaleTurn => "stale_turn",
        TurnEngineRejectionReason.DeadlineNotSet => "deadline_not_set",
        TurnEngineRejectionReason.TurnNotExpired => "turn_not_expired",
        _ => throw new InvalidOperationException($"Unknown turn rejection reason '{Reason}'."),
    };
}
