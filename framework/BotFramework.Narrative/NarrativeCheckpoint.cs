namespace BotFramework.Narrative;

/// <summary>Persisted, frontend-resumable point in a narrative projection.</summary>
public sealed record NarrativeCheckpoint
{
    public NarrativeCheckpoint(
        string checkpointId,
        string? resumeToken,
        IReadOnlyDictionary<string, string?>? data = null)
    {
        CheckpointId = NarrativeValue.Required(checkpointId, nameof(checkpointId));
        ResumeToken = resumeToken is null ? null : NarrativeValue.Required(resumeToken, nameof(resumeToken));
        Data = NarrativeValue.CopyMetadata(data, nameof(data));
    }

    public string CheckpointId { get; }

    public string? ResumeToken { get; }

    public IReadOnlyDictionary<string, string?> Data { get; }
}
