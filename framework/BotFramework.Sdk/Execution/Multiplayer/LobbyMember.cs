using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// One transport-neutral lobby membership. Team and role identifiers are
/// opaque game-owned strings, avoiding a shared enum that would constrain game
/// design or leak a transport identity.
/// </summary>
public sealed record LobbyMember<TPlayerId>
    where TPlayerId : notnull
{
    public LobbyMember(
        TPlayerId playerId,
        LobbyMemberKind kind,
        int? seat = null,
        bool isReady = false,
        string? teamId = null,
        IEnumerable<string>? roles = null)
    {
        ArgumentNullException.ThrowIfNull(playerId);
        if (kind == LobbyMemberKind.Player && seat is null)
            throw new ArgumentException("A player requires a seat.", nameof(seat));
        if (kind == LobbyMemberKind.Spectator && seat is not null)
            throw new ArgumentException("A spectator cannot occupy a player seat.", nameof(seat));
        if (seat is < 0)
            throw new ArgumentOutOfRangeException(nameof(seat), "A seat cannot be negative.");
        if (kind == LobbyMemberKind.Spectator && isReady)
            throw new ArgumentException("A spectator cannot be marked ready.", nameof(isReady));
        if (kind == LobbyMemberKind.Spectator && teamId is not null)
            throw new ArgumentException("A spectator cannot be assigned to a player team.", nameof(teamId));

        PlayerId = playerId;
        Kind = kind;
        Seat = seat;
        IsReady = isReady;
        TeamId = teamId is null ? null : RequireText(teamId, nameof(teamId));
        Roles = CopyRoles(roles);
    }

    public TPlayerId PlayerId { get; }

    public LobbyMemberKind Kind { get; }

    public int? Seat { get; }

    public bool IsReady { get; }

    public string? TeamId { get; }

    public IReadOnlyList<string> Roles { get; }

    private static ReadOnlyCollection<string> CopyRoles(IEnumerable<string>? roles)
    {
        var copy = (roles ?? []).Select(role => RequireText(role, nameof(roles))).ToArray();
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Roles must be unique for one member.", nameof(roles));
        return new ReadOnlyCollection<string>(copy);
    }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;
}
