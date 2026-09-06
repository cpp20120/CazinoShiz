using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>
/// Stores a frontend-resumable point in a narrative. The owning sink decides
/// where it is persisted; the game only supplies semantic data.
/// </summary>
public sealed record NarrativeCheckpointEffect : NarrativeEffect
{
    public NarrativeCheckpointEffect(
        NarrativeAddress target,
        string checkpointId,
        string? resumeToken = null,
        IReadOnlyDictionary<string, string?>? data = null)
        : base(target)
    {
        CheckpointId = NarrativeValue.Required(checkpointId, nameof(checkpointId));
        ResumeToken = resumeToken is null ? null : NarrativeValue.Required(resumeToken, nameof(resumeToken));
        Data = NarrativeValue.CopyMetadata(data, nameof(data));
    }

    public string CheckpointId { get; }

    public string? ResumeToken { get; }

    public IReadOnlyDictionary<string, string?> Data { get; }

    public override GameCapability RequiredCapability => NarrativeCapabilities.Checkpoints;
}
