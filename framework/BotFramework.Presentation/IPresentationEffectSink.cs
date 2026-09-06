namespace BotFramework.Presentation;

/// <summary>
/// Implemented by a Web, Telegram, test or another frontend adapter. The sink
/// owns rendering, delivery and idempotency of durable presentation output.
/// </summary>
public interface IPresentationEffectSink
{
    Task ApplyAsync(
        IReadOnlyList<PresentationEffect> effects,
        PresentationDeliveryContext context,
        CancellationToken ct);
}
