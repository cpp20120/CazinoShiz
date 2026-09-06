using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Serializable framework-owned part of a multiplayer aggregate. Embed it in a
/// game state along with its board, scoring and game-specific start rules.
/// </summary>
public sealed record LobbyState<TPlayerId>
    where TPlayerId : notnull
{
    private readonly IReadOnlyList<LobbyMember<TPlayerId>> _players;
    private readonly IReadOnlyList<LobbyMember<TPlayerId>> _spectators;

    public LobbyState(LobbyStatus status, IEnumerable<LobbyMember<TPlayerId>> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var copy = members.ToArray();
        if (copy.Any(static member => member is null))
            throw new ArgumentException("Lobby members cannot contain null.", nameof(members));
        EnsureUnique(copy);
        EnsureSeats(copy);

        Status = status;
        Members = new ReadOnlyCollection<LobbyMember<TPlayerId>>(copy);
        _players = new ReadOnlyCollection<LobbyMember<TPlayerId>>(copy
            .Where(static member => member.Kind == LobbyMemberKind.Player)
            .ToArray());
        _spectators = new ReadOnlyCollection<LobbyMember<TPlayerId>>(copy
            .Where(static member => member.Kind == LobbyMemberKind.Spectator)
            .ToArray());
    }

    public LobbyStatus Status { get; }

    public IReadOnlyList<LobbyMember<TPlayerId>> Members { get; }

    [JsonIgnore]
    public IReadOnlyList<LobbyMember<TPlayerId>> Players => _players;

    [JsonIgnore]
    public IReadOnlyList<LobbyMember<TPlayerId>> Spectators => _spectators;

    private static void EnsureUnique(LobbyMember<TPlayerId>[] members)
    {
        if (members.Select(member => member.PlayerId).Distinct().Count() != members.Length)
            throw new ArgumentException("A lobby player can occur only once.", nameof(members));
    }

    private static void EnsureSeats(LobbyMember<TPlayerId>[] members)
    {
        var seats = members.Where(static member => member.Seat is not null).Select(member => member.Seat!.Value).ToArray();
        if (seats.Distinct().Count() != seats.Length)
            throw new ArgumentException("A lobby seat can be occupied only once.", nameof(members));
    }
}
