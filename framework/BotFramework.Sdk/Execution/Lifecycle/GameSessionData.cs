using System.Text.Json;

namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Shared validation and JSON normalization for session implementations.</summary>
public static class GameSessionData
{
    public static string Normalize(string? value)
    {
        var json = string.IsNullOrWhiteSpace(value) ? "{}" : value;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetRawText();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Game session data must be valid JSON.", nameof(value), exception);
        }
    }

    public static string RequireId(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;
}
