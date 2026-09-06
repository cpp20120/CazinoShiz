namespace BotFramework.Narrative;

/// <summary>Read port for the framework-owned, resumable narrative projection.</summary>
public interface INarrativeProjectionStore
{
    Task<NarrativeProjection?> GetAsync(NarrativeProjectionKey key, CancellationToken ct);
}
