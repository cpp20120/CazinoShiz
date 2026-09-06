namespace BotFramework.Narrative;

/// <summary>
/// Implemented by a Web, Telegram, test or another frontend adapter. The sink
/// owns rendering, delivery and any projection storage for narrative effects.
/// </summary>
public interface INarrativeEffectSink
{
    Task ApplyAsync(
        IReadOnlyList<NarrativeEffect> effects,
        NarrativeDeliveryContext context,
        CancellationToken ct);
}
