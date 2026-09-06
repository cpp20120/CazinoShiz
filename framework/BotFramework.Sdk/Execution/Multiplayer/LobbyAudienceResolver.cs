namespace BotFramework.Sdk.Execution;

/// <summary>
/// Resolves public/private/team/role selectors from a lobby snapshot. Games can
/// use it to create recipient-specific effects, while frontends may retain the
/// original <see cref="GameAudience"/> as semantic routing information.
/// </summary>
public sealed class LobbyAudienceResolver<TPlayerId>(
    Func<TPlayerId, string> playerIdFormatter,
    IEqualityComparer<TPlayerId>? comparer = null)
    where TPlayerId : notnull
{
    private readonly IEqualityComparer<TPlayerId> _comparer = comparer ?? EqualityComparer<TPlayerId>.Default;

    public GameAudienceResolution<TPlayerId> Resolve(
        LobbyState<TPlayerId> state,
        GameAudience audience)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(playerIdFormatter);

        if (audience.Kind == GameAudienceKind.Public)
            return new(audience, true, []);

        var recipients = audience.Kind switch
        {
            GameAudienceKind.Participants => state.Players.Select(member => member.PlayerId),
            GameAudienceKind.Spectators => state.Spectators.Select(member => member.PlayerId),
            GameAudienceKind.Player => state.Members
                .Where(member => string.Equals(playerIdFormatter(member.PlayerId), audience.SelectorId, StringComparison.Ordinal))
                .Select(member => member.PlayerId),
            GameAudienceKind.Team => state.Players
                .Where(member => string.Equals(member.TeamId, audience.SelectorId, StringComparison.Ordinal))
                .Select(member => member.PlayerId),
            GameAudienceKind.Role => state.Members
                .Where(member => member.Roles.Contains(audience.SelectorId!, StringComparer.Ordinal))
                .Select(member => member.PlayerId),
            _ => throw new InvalidOperationException($"Unknown game audience '{audience.Kind}'."),
        };

        return new(audience, false, recipients.Distinct(_comparer).ToArray());
    }

    /// <summary>Checks whether one member is allowed to see the selected audience.</summary>
    public bool CanView(
        LobbyState<TPlayerId> state,
        TPlayerId viewerId,
        GameAudience audience)
    {
        ArgumentNullException.ThrowIfNull(viewerId);
        var resolution = Resolve(state, audience);
        return resolution.IsPublic || resolution.Recipients.Any(recipient => _comparer.Equals(recipient, viewerId));
    }
}
