namespace BotFramework.Sdk.Execution;

/// <summary>
/// Deterministic random operations backed only by entropy supplied in a game
/// action input. Implementations must never draw from process-global randomness.
/// </summary>
public interface IGameRandom
{
    double NextUnit(string name);

    int NextInt(string name, int exclusiveUpperBound);

    bool Chance(string name, double probability);

    T Pick<T>(string name, IReadOnlyList<T> values);

    IReadOnlyList<T> Shuffle<T>(string namePrefix, IReadOnlyList<T> values);

    GameRandomTrace Trace { get; }
}
