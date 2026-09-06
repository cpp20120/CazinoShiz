namespace BotFramework.Sdk.Execution;

/// <summary>Stable generic reasons why a lobby operation was rejected.</summary>
public enum LobbyRejectionReason
{
    LobbyNotOpen,
    LobbyIsTerminal,
    PlayerAlreadyJoined,
    PlayerNotJoined,
    PlayerLimitReached,
    SpectatorLimitReached,
    SeatUnavailable,
    MemberIsSpectator,
    MemberIsPlayer,
    NotEnoughPlayers,
    PlayersNotReady,
    RoleAlreadyGranted,
    RoleNotGranted,
}
