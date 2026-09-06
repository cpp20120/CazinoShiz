using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>
/// Declarative frontend output for ordinary game UI. Presentation effects are
/// durable and transport-neutral: they never contain Telegram, HTML or browser
/// component types.
/// </summary>
public abstract record PresentationEffect(PresentationAddress Target) : IGameCapabilityEffect, IDurableGameEffect
{
    public PresentationAddress Target { get; } = Target ?? throw new ArgumentNullException(nameof(Target));

    public abstract GameCapability RequiredCapability { get; }
}
