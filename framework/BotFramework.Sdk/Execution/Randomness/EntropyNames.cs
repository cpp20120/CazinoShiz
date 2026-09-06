namespace BotFramework.Sdk.Execution;

/// <summary>Builds stable descriptor entropy names for repeated random operations.</summary>
public static class EntropyNames
{
    /// <summary>
    /// Returns names for a Fisher-Yates shuffle of <paramref name="itemCount"/>
    /// items. A shuffle of zero or one item consumes no entropy values.
    /// </summary>
    public static IReadOnlyList<string> ForShuffle(string prefix, int itemCount)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("An entropy prefix is required.", nameof(prefix));
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);

        return Enumerable.Range(0, Math.Max(0, itemCount - 1))
            .Select(index => $"{prefix}:{index}")
            .ToArray();
    }

    /// <summary>Returns stable names for a fixed number of independent draws.</summary>
    public static IReadOnlyList<string> ForDraws(string prefix, int count)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("An entropy prefix is required.", nameof(prefix));
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return Enumerable.Range(0, count).Select(index => $"{prefix}:{index}").ToArray();
    }
}
