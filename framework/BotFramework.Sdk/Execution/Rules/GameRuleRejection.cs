using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// A stable, machine-readable rule violation. Detail and metadata are optional
/// diagnostics; adapters should normally render the stable <see cref="Code"/>
/// through their own localization.
/// </summary>
public sealed record GameRuleRejection
{
    public GameRuleRejection(
        GameRuleRejectionKind kind,
        string? detail = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? customCode = null)
    {
        if (kind == GameRuleRejectionKind.Custom && string.IsNullOrWhiteSpace(customCode))
            throw new ArgumentException("A custom rule rejection requires a code.", nameof(customCode));
        if (kind != GameRuleRejectionKind.Custom && customCode is not null)
            throw new ArgumentException("Only custom rule rejections accept a custom code.", nameof(customCode));

        Kind = kind;
        Detail = detail;
        CustomCode = customCode;
        Metadata = new ReadOnlyDictionary<string, string>(CopyMetadata(metadata));
    }

    public GameRuleRejectionKind Kind { get; }

    public string? Detail { get; }

    public string? CustomCode { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public string Code => Kind switch
    {
        GameRuleRejectionKind.Forbidden => "forbidden",
        GameRuleRejectionKind.GameNotActive => "game_not_active",
        GameRuleRejectionKind.PlayerNotJoined => "player_not_joined",
        GameRuleRejectionKind.NotYourTurn => "not_your_turn",
        GameRuleRejectionKind.InsufficientPlayers => "insufficient_players",
        GameRuleRejectionKind.LobbyNotReady => "lobby_not_ready",
        GameRuleRejectionKind.PhaseClosed => "phase_closed",
        GameRuleRejectionKind.DeadlineExpired => "deadline_expired",
        GameRuleRejectionKind.StaleInput => "stale_input",
        GameRuleRejectionKind.PermissionDenied => "permission_denied",
        GameRuleRejectionKind.DuplicateAction => "duplicate_action",
        GameRuleRejectionKind.InvalidInput => "invalid_input",
        GameRuleRejectionKind.Custom => CustomCode!,
        _ => throw new InvalidOperationException($"Unknown game rule rejection '{Kind}'."),
    };

    public static GameRuleRejection NotYourTurn { get; } = new(GameRuleRejectionKind.NotYourTurn);

    public static GameRuleRejection InsufficientPlayers { get; } = new(GameRuleRejectionKind.InsufficientPlayers);

    public static GameRuleRejection PhaseClosed { get; } = new(GameRuleRejectionKind.PhaseClosed);

    public static GameRuleRejection PlayerNotJoined { get; } = new(GameRuleRejectionKind.PlayerNotJoined);

    public static GameRuleRejection GameNotActive { get; } = new(GameRuleRejectionKind.GameNotActive);

    public static GameRuleRejection DeadlineExpired { get; } = new(GameRuleRejectionKind.DeadlineExpired);

    public static GameRuleRejection StaleInput { get; } = new(GameRuleRejectionKind.StaleInput);

    public static GameRuleRejection InvalidInput { get; } = new(GameRuleRejectionKind.InvalidInput);

    public static GameRuleRejection Custom(
        string code,
        string? detail = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(GameRuleRejectionKind.Custom, detail, metadata, code);

    private static Dictionary<string, string> CopyMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata is null)
            return copy;
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Rule-rejection metadata keys and values are required.", nameof(metadata));
            if (!copy.TryAdd(key, value))
                throw new ArgumentException("Rule-rejection metadata keys must be unique.", nameof(metadata));
        }
        return copy;
    }
}
