namespace BotFramework.Narrative;

internal static class NarrativeValue
{
    public static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;

    public static IReadOnlyDictionary<string, string?> CopyMetadata(
        IReadOnlyDictionary<string, string?>? values,
        string parameterName)
    {
        if (values is null || values.Count == 0)
            return EmptyMetadata.Instance;

        var copy = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
            copy.Add(Required(key, parameterName), value);
        return copy;
    }

    public static IReadOnlyList<T> CopyRequired<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("At least one value is required.", parameterName);
        return copy.Any(static _ => false) ? throw new ArgumentException("Values cannot contain null.", parameterName) : copy;
    }

    private static class EmptyMetadata
    {
        public static readonly IReadOnlyDictionary<string, string?> Instance =
            new Dictionary<string, string?>(StringComparer.Ordinal);
    }
}
