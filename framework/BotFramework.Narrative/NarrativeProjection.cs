namespace BotFramework.Narrative;

/// <summary>
/// Read model shared by narrative frontends. Scenes and dialog remain rendered
/// output; flags, checkpoint and active choice are the resumable state.
/// </summary>
public sealed record NarrativeProjection
{
    public NarrativeProjection(
        NarrativeProjectionKey key,
        IReadOnlyDictionary<string, bool>? flags,
        NarrativeCheckpoint? checkpoint,
        NarrativeActiveChoice? activeChoice,
        long revision,
        DateTimeOffset updatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);

        Key = key ?? throw new ArgumentNullException(nameof(key));
        Flags = CopyFlags(flags);
        Checkpoint = checkpoint;
        ActiveChoice = activeChoice;
        Revision = revision;
        UpdatedAt = updatedAt;
    }

    public NarrativeProjectionKey Key { get; }

    /// <summary>Only currently set flags are present.</summary>
    public IReadOnlyDictionary<string, bool> Flags { get; }

    public NarrativeCheckpoint? Checkpoint { get; }

    /// <summary>Null when no choice exists or its expiry has passed.</summary>
    public NarrativeActiveChoice? ActiveChoice { get; }

    public long Revision { get; }

    public DateTimeOffset UpdatedAt { get; }

    private static Dictionary<string, bool> CopyFlags(IReadOnlyDictionary<string, bool>? flags)
    {
        if (flags is null || flags.Count == 0)
            return new Dictionary<string, bool>(StringComparer.Ordinal);

        var copy = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (flag, value) in flags)
            copy.Add(NarrativeValue.Required(flag, nameof(flags)), value);
        return copy;
    }
}
