namespace BotFramework.Sdk.Execution;

/// <summary>
/// Deterministic random facade over <see cref="EntropyValue"/>. It maps named
/// values supplied by the executor to common outcomes and records exactly the
/// values consumed by a rule; it never produces fresh entropy itself.
/// </summary>
public sealed class EntropyGameRandom(EntropyValue entropy) : IGameRandom
{
    private readonly EntropyValue _entropy = entropy ?? throw new ArgumentNullException(nameof(entropy));
    private readonly List<GameRandomDraw> _draws = [];
    private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);

    public GameRandomTrace Trace => _draws.Count == 0 ? GameRandomTrace.Empty : new(_draws);

    public double NextUnit(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An entropy name is required.", nameof(name));
        if (!_consumed.Add(name))
            throw new InvalidOperationException($"Entropy value '{name}' was already consumed.");

        var value = _entropy.GetDouble(name);
        _draws.Add(new GameRandomDraw(name, value));
        return value;
    }

    public int NextInt(string name, int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(exclusiveUpperBound, 0);
        return (int)(NextUnit(name) * exclusiveUpperBound);
    }

    public bool Chance(string name, double probability)
    {
        if (probability is < 0 or > 1 || double.IsNaN(probability))
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be in [0, 1].");
        return NextUnit(name) < probability;
    }

    public T Pick<T>(string name, IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("Cannot select from an empty set.", nameof(values));
        return values[NextInt(name, values.Count)];
    }

    public IReadOnlyList<T> Shuffle<T>(string namePrefix, IReadOnlyList<T> values)
    {
        if (string.IsNullOrWhiteSpace(namePrefix))
            throw new ArgumentException("An entropy prefix is required.", nameof(namePrefix));
        ArgumentNullException.ThrowIfNull(values);

        var shuffled = values.ToArray();
        var draw = 0;
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swap = NextInt($"{namePrefix}:{draw++}", index + 1);
            (shuffled[index], shuffled[swap]) = (shuffled[swap], shuffled[index]);
        }
        return Array.AsReadOnly(shuffled);
    }
}
