using System.Text.Json;

namespace BotFramework.Sdk.Execution;

/// <summary>Typed JSON captured in one auditable game execution entry.</summary>
public sealed record GameReplayPayload
{
    public GameReplayPayload(string typeName, string json)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A replay payload type is required.", nameof(typeName));
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("A replay payload JSON document is required.", nameof(json));
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Replay payload must be valid JSON.", nameof(json), exception);
        }

        TypeName = typeName;
        Json = json;
    }

    public string TypeName { get; }

    public string Json { get; }
}
