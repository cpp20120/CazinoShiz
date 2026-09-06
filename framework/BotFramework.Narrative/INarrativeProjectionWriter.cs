namespace BotFramework.Narrative;

/// <summary>
/// Applies the persisted portion of a narrative effect. Frontends normally use
/// the registered Host implementation; the port also permits a custom store.
/// </summary>
public interface INarrativeProjectionWriter
{
    Task ApplyAsync(NarrativeEffect effect, NarrativeDeliveryContext context, CancellationToken ct);
}
