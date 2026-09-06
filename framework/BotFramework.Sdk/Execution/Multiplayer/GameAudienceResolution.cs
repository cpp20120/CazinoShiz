namespace BotFramework.Sdk.Execution;

/// <summary>
/// Result of resolving a visibility selector against a lobby snapshot. A public
/// audience is intentionally represented without recipients: it targets the
/// conversation rather than a player-specific delivery channel.
/// </summary>
public sealed record GameAudienceResolution<TPlayerId>(
    GameAudience Audience,
    bool IsPublic,
    IReadOnlyList<TPlayerId> Recipients)
    where TPlayerId : notnull;
