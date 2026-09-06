namespace BotFramework.Sdk.Execution;

/// <summary>A machine-readable lobby-operation rejection.</summary>
public sealed record LobbyRejection(LobbyRejectionReason Reason)
{
    public string Code => Reason switch
    {
        LobbyRejectionReason.LobbyNotOpen => "lobby_not_open",
        LobbyRejectionReason.LobbyIsTerminal => "lobby_is_terminal",
        LobbyRejectionReason.PlayerAlreadyJoined => "player_already_joined",
        LobbyRejectionReason.PlayerNotJoined => "player_not_joined",
        LobbyRejectionReason.PlayerLimitReached => "player_limit_reached",
        LobbyRejectionReason.SpectatorLimitReached => "spectator_limit_reached",
        LobbyRejectionReason.SeatUnavailable => "seat_unavailable",
        LobbyRejectionReason.MemberIsSpectator => "member_is_spectator",
        LobbyRejectionReason.MemberIsPlayer => "member_is_player",
        LobbyRejectionReason.NotEnoughPlayers => "insufficient_players",
        LobbyRejectionReason.PlayersNotReady => "players_not_ready",
        LobbyRejectionReason.RoleAlreadyGranted => "role_already_granted",
        LobbyRejectionReason.RoleNotGranted => "role_not_granted",
        _ => throw new InvalidOperationException($"Unknown lobby rejection reason '{Reason}'."),
    };
}
