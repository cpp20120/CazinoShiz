using System.Text.Json;

namespace BotFramework.Sdk.Execution;

/// <summary>Validation and JSON normalization shared by interaction contracts.</summary>
public static class GameInputRequestData
{
    public static string RequireId(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;

    public static string NormalizeJson(string? value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Input request payload must be valid JSON.", nameof(value), exception);
        }
    }

    public static IReadOnlyList<string> NormalizeAllowedValues(IEnumerable<string>? values)
    {
        var copy = (values ?? []).ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Allowed values cannot be empty.", nameof(values));
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Allowed values must be unique.", nameof(values));
        return copy;
    }
}
