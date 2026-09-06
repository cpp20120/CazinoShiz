namespace BotFramework.Sdk.Execution;

/// <summary>
/// Transport-neutral selector for a public or private game view/effect. IDs are
/// opaque game values: an adapter maps the selected members to its recipient
/// model without introducing Telegram or Web identifiers into game rules.
/// </summary>
public sealed record GameAudience
{
    public GameAudience(GameAudienceKind kind, string? selectorId = null)
    {
        if (kind is GameAudienceKind.Player or GameAudienceKind.Team or GameAudienceKind.Role)
        {
            if (string.IsNullOrWhiteSpace(selectorId))
                throw new ArgumentException("This audience requires a selector id.", nameof(selectorId));
        }
        else if (selectorId is not null)
        {
            throw new ArgumentException("This audience does not accept a selector id.", nameof(selectorId));
        }

        Kind = kind;
        SelectorId = selectorId;
    }

    public GameAudienceKind Kind { get; }

    public string? SelectorId { get; }

    public static GameAudience Public { get; } = new(GameAudienceKind.Public);

    public static GameAudience Participants { get; } = new(GameAudienceKind.Participants);

    public static GameAudience Spectators { get; } = new(GameAudienceKind.Spectators);

    public static GameAudience Player(string playerId) => new(GameAudienceKind.Player, playerId);

    public static GameAudience Team(string teamId) => new(GameAudienceKind.Team, teamId);

    public static GameAudience Role(string roleId) => new(GameAudienceKind.Role, roleId);
}
