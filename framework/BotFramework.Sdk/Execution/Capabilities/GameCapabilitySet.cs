namespace BotFramework.Sdk.Execution;

/// <summary>Immutable set of capabilities declared by a game or provided by a runtime.</summary>
public sealed class GameCapabilitySet
{
    public static readonly GameCapabilitySet Empty = new([]);

    public GameCapabilitySet(IEnumerable<GameCapability>? capabilities)
    {
        var values = new Dictionary<string, GameCapability>(StringComparer.Ordinal);
        foreach (var capability in capabilities ?? [])
        {
            if (capability is null)
                throw new ArgumentNullException(nameof(capabilities));
            if (!values.TryAdd(capability.Id, capability))
                throw new ArgumentException($"Capability '{capability.Id}' is declared more than once.", nameof(capabilities));
        }

        Values = values.Values.ToArray();
    }

    public IReadOnlyList<GameCapability> Values { get; }

    public bool IsEmpty => Values.Count == 0;

    public bool Contains(GameCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return Values.Any(value => string.Equals(value.Id, capability.Id, StringComparison.Ordinal));
    }

    public IReadOnlyList<GameCapability> MissingFrom(GameCapabilitySet available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return Values.Where(capability => !available.Contains(capability)).ToArray();
    }

    public GameCapabilitySet Union(IEnumerable<GameCapability> capabilities) =>
        new(Values.Concat(capabilities ?? throw new ArgumentNullException(nameof(capabilities))));
}
