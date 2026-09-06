namespace BotFramework.Sdk.Execution;

/// <summary>
/// Transport-neutral manifest for a game. It exposes discoverable metadata only;
/// game rules and presentation remain owned by the module and transport adapters.
/// </summary>
public sealed class GameDefinition
{
    public GameDefinition(
        string gameId,
        string displayName,
        GameCapabilities capabilities = GameCapabilities.None,
        GameStakeLimits? stakeLimits = null,
        string? dailyQuotaId = null,
        IEnumerable<string>? entropyNames = null,
        IEnumerable<GameCapability>? requiredCapabilities = null)
    {
        GameId = RequireText(gameId, nameof(gameId));
        DisplayName = RequireText(displayName, nameof(displayName));
        Capabilities = capabilities;
        StakeLimits = stakeLimits;
        DailyQuotaId = dailyQuotaId is null ? null : RequireText(dailyQuotaId, nameof(dailyQuotaId));
        EntropyNames = ValidateEntropyNames(entropyNames);
        RequiredCapabilities = new GameCapabilitySet(requiredCapabilities);
    }

    public string GameId { get; }
    public string DisplayName { get; }
    public GameCapabilities Capabilities { get; }
    public GameCapabilities ExecutionStyles => Capabilities;
    public GameStakeLimits? StakeLimits { get; }
    public string? DailyQuotaId { get; }
    public IReadOnlyList<string> EntropyNames { get; }
    public GameCapabilitySet RequiredCapabilities { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;

    private static string[] ValidateEntropyNames(IEnumerable<string>? names)
    {
        if (names is null)
            return [];

        var copy = names.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Entropy names cannot be empty.", nameof(names));
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Entropy names must be unique.", nameof(names));
        return copy;
    }
}
