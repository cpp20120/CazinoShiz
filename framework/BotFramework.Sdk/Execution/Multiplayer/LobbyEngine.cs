namespace BotFramework.Sdk.Execution;

/// <summary>
/// Pure, transport-neutral lobby state machine. It owns generic membership,
/// seats, readiness, teams and roles; a game owns authorization, invite codes,
/// matchmaking search and any extra condition required to start.
/// </summary>
public sealed class LobbyEngine<TPlayerId>(
    LobbyConfiguration configuration,
    IEqualityComparer<TPlayerId>? comparer = null)
    where TPlayerId : notnull
{
    private readonly IEqualityComparer<TPlayerId> _comparer = comparer ?? EqualityComparer<TPlayerId>.Default;

    public LobbyConfiguration Configuration { get; } = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>Creates an empty lobby in its open state.</summary>
    public LobbyState<TPlayerId> Create() => new(LobbyStatus.Open, []);

    /// <summary>Adds a playing member at a requested or first free numbered seat.</summary>
    public LobbyTransition<TPlayerId> JoinPlayer(
        LobbyState<TPlayerId> state,
        TPlayerId playerId,
        int? requestedSeat = null,
        string? teamId = null)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!RequireOpen(state, out var rejection))
            return Reject(state, rejection);
        if (FindMember(state, playerId) is not null)
            return Reject(state, LobbyRejectionReason.PlayerAlreadyJoined);
        if (state.Players.Count >= Configuration.MaximumPlayers)
            return Reject(state, LobbyRejectionReason.PlayerLimitReached);

        var seat = FindSeat(state, requestedSeat);
        if (seat is null)
            return Reject(state, LobbyRejectionReason.SeatUnavailable);
        return Accept(new(state.Status, [.. state.Members, new LobbyMember<TPlayerId>(
            playerId, LobbyMemberKind.Player, seat, teamId: teamId)]));
    }

    /// <summary>Adds an observer that does not take a player seat or readiness slot.</summary>
    public LobbyTransition<TPlayerId> JoinSpectator(LobbyState<TPlayerId> state, TPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!RequireOpen(state, out var rejection))
            return Reject(state, rejection);
        if (FindMember(state, playerId) is not null)
            return Reject(state, LobbyRejectionReason.PlayerAlreadyJoined);
        if (Configuration.MaximumSpectators is { } maximum && state.Spectators.Count >= maximum)
            return Reject(state, LobbyRejectionReason.SpectatorLimitReached);

        return Accept(new(state.Status, [.. state.Members, new LobbyMember<TPlayerId>(
            playerId, LobbyMemberKind.Spectator)]));
    }

    /// <summary>Removes a member before the match begins.</summary>
    public LobbyTransition<TPlayerId> Leave(LobbyState<TPlayerId> state, TPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!RequireOpen(state, out var rejection))
            return Reject(state, rejection);
        if (FindMember(state, playerId) is null)
            return Reject(state, LobbyRejectionReason.PlayerNotJoined);

        return Accept(new(state.Status, state.Members.Where(member => !_comparer.Equals(member.PlayerId, playerId))));
    }

    /// <summary>Sets the ready state of a player. Spectators never participate in readiness.</summary>
    public LobbyTransition<TPlayerId> SetReady(
        LobbyState<TPlayerId> state,
        TPlayerId playerId,
        bool ready = true)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!RequireOpen(state, out var rejection))
            return Reject(state, rejection);
        var member = FindMember(state, playerId);
        if (member is null)
            return Reject(state, LobbyRejectionReason.PlayerNotJoined);
        if (member.Kind != LobbyMemberKind.Player)
            return Reject(state, LobbyRejectionReason.MemberIsSpectator);
        if (member.IsReady == ready)
            return Accept(state);

        return Accept(ReplaceMember(state, Recreate(member, isReady: ready)));
    }

    /// <summary>Assigns a game-defined opaque team identifier to a player.</summary>
    public LobbyTransition<TPlayerId> AssignTeam(
        LobbyState<TPlayerId> state,
        TPlayerId playerId,
        string? teamId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (!RequireMutable(state, out var rejection))
            return Reject(state, rejection);
        var member = FindMember(state, playerId);
        if (member is null)
            return Reject(state, LobbyRejectionReason.PlayerNotJoined);
        if (member.Kind != LobbyMemberKind.Player)
            return Reject(state, LobbyRejectionReason.MemberIsSpectator);
        if (string.IsNullOrWhiteSpace(teamId))
            return Accept(ReplaceMember(state, RecreateWithTeam(member, null)));

        return Accept(ReplaceMember(state, RecreateWithTeam(member, teamId)));
    }

    /// <summary>Grants a game-defined role to a player or spectator.</summary>
    public LobbyTransition<TPlayerId> GrantRole(
        LobbyState<TPlayerId> state,
        TPlayerId playerId,
        string roleId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        RequireRole(roleId);
        if (!RequireMutable(state, out var rejection))
            return Reject(state, rejection);
        var member = FindMember(state, playerId);
        if (member is null)
            return Reject(state, LobbyRejectionReason.PlayerNotJoined);
        if (member.Roles.Contains(roleId, StringComparer.Ordinal))
            return Reject(state, LobbyRejectionReason.RoleAlreadyGranted);

        return Accept(ReplaceMember(state, Recreate(member, roles: [.. member.Roles, roleId])));
    }

    /// <summary>Revokes a game-defined role from a player or spectator.</summary>
    public LobbyTransition<TPlayerId> RevokeRole(
        LobbyState<TPlayerId> state,
        TPlayerId playerId,
        string roleId)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        RequireRole(roleId);
        if (!RequireMutable(state, out var rejection))
            return Reject(state, rejection);
        var member = FindMember(state, playerId);
        if (member is null)
            return Reject(state, LobbyRejectionReason.PlayerNotJoined);
        if (!member.Roles.Contains(roleId, StringComparer.Ordinal))
            return Reject(state, LobbyRejectionReason.RoleNotGranted);

        return Accept(ReplaceMember(state, Recreate(member, roles: member.Roles
            .Where(role => !string.Equals(role, roleId, StringComparison.Ordinal))
            .ToArray())));
    }

    /// <summary>
    /// Starts when the generic capacity and readiness invariants are met. Apply
    /// any game-specific guard (for example one player per team) before calling.
    /// </summary>
    public LobbyTransition<TPlayerId> Start(LobbyState<TPlayerId> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != LobbyStatus.Open)
            return Reject(state, state.Status == LobbyStatus.Cancelled
                ? LobbyRejectionReason.LobbyIsTerminal
                : LobbyRejectionReason.LobbyNotOpen);
        if (state.Players.Count < Configuration.MinimumPlayers)
            return Reject(state, LobbyRejectionReason.NotEnoughPlayers);
        if (Configuration.RequireAllPlayersReady && state.Players.Any(static player => !player.IsReady))
            return Reject(state, LobbyRejectionReason.PlayersNotReady);

        return Accept(new(LobbyStatus.Started, state.Members));
    }

    /// <summary>Cancels a nonterminal lobby or match without defining compensation.</summary>
    public LobbyTransition<TPlayerId> Cancel(LobbyState<TPlayerId> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Status == LobbyStatus.Cancelled
            ? Reject(state, LobbyRejectionReason.LobbyIsTerminal)
            : Accept(new(LobbyStatus.Cancelled, state.Members));
    }

    private LobbyState<TPlayerId> ReplaceMember(
        LobbyState<TPlayerId> state,
        LobbyMember<TPlayerId> replacement) =>
        new(state.Status, state.Members.Select(member => _comparer.Equals(member.PlayerId, replacement.PlayerId)
            ? replacement
            : member));

    private static LobbyMember<TPlayerId> Recreate(
        LobbyMember<TPlayerId> member,
        bool? isReady = null,
        IReadOnlyList<string>? roles = null) =>
        new(
            member.PlayerId,
            member.Kind,
            member.Seat,
            isReady ?? member.IsReady,
            member.TeamId,
            roles ?? member.Roles);

    private static LobbyMember<TPlayerId> RecreateWithTeam(
        LobbyMember<TPlayerId> member,
        string? teamId) =>
        new(member.PlayerId, member.Kind, member.Seat, member.IsReady, teamId, member.Roles);

    private LobbyMember<TPlayerId>? FindMember(LobbyState<TPlayerId> state, TPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Members.FirstOrDefault(member => _comparer.Equals(member.PlayerId, playerId));
    }

    private int? FindSeat(LobbyState<TPlayerId> state, int? requestedSeat)
    {
        var occupied = state.Players.Select(player => player.Seat!.Value).ToHashSet();
        if (requestedSeat is { } requested)
            return requested >= 0 && requested < Configuration.SeatCount && !occupied.Contains(requested)
                ? requested
                : null;
        for (var seat = 0; seat < Configuration.SeatCount; seat++)
        {
            if (!occupied.Contains(seat))
                return seat;
        }
        return null;
    }

    private static bool RequireOpen(LobbyState<TPlayerId>? state, out LobbyRejectionReason rejection)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status == LobbyStatus.Open)
        {
            rejection = default;
            return true;
        }
        rejection = state.Status == LobbyStatus.Cancelled
            ? LobbyRejectionReason.LobbyIsTerminal
            : LobbyRejectionReason.LobbyNotOpen;
        return false;
    }

    private static bool RequireMutable(LobbyState<TPlayerId>? state, out LobbyRejectionReason rejection)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status != LobbyStatus.Cancelled)
        {
            rejection = default;
            return true;
        }
        rejection = LobbyRejectionReason.LobbyIsTerminal;
        return false;
    }

    private static void RequireRole(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId))
            throw new ArgumentException("Role id is required.", nameof(roleId));
    }

    private static LobbyTransition<TPlayerId> Accept(LobbyState<TPlayerId> state) =>
        new(LobbyTransitionStatus.Applied, state);

    private static LobbyTransition<TPlayerId> Reject(
        LobbyState<TPlayerId>? state,
        LobbyRejectionReason reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(LobbyTransitionStatus.Rejected, state, new LobbyRejection(reason));
    }
}
