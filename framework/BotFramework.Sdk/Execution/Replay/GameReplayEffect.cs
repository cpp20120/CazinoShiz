namespace BotFramework.Sdk.Execution;

/// <summary>One declared effect or domain event captured by execution history.</summary>
public sealed record GameReplayEffect
{
    public GameReplayEffect(string category, int index, GameReplayPayload payload)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("A replay effect category is required.", nameof(category));
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentNullException.ThrowIfNull(payload);

        Category = category;
        Index = index;
        Payload = payload;
    }

    public string Category { get; }

    public int Index { get; }

    public GameReplayPayload Payload { get; }
}
