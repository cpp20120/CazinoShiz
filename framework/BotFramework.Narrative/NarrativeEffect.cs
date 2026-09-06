using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>
/// Declarative narrative output. It contains no Telegram, HTTP, markup or UI
/// types, so one game can be rendered by any frontend.
/// </summary>
public abstract record NarrativeEffect(NarrativeAddress Target) : IGameCapabilityEffect, IDurableGameEffect
{
    public NarrativeAddress Target { get; } = Target ?? throw new ArgumentNullException(nameof(Target));

    public abstract GameCapability RequiredCapability { get; }
}
