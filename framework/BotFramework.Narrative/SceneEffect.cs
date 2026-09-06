using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>Moves a recipient to a named narrative scene.</summary>
public sealed record SceneEffect : NarrativeEffect
{
    public SceneEffect(
        NarrativeAddress target,
        string sceneId,
        NarrativeText title,
        NarrativeText? body = null,
        NarrativeSceneMode mode = NarrativeSceneMode.Replace)
        : base(target)
    {
        SceneId = NarrativeValue.Required(sceneId, nameof(sceneId));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Body = body;
        Mode = mode;
    }

    public string SceneId { get; }

    public NarrativeText Title { get; }

    public NarrativeText? Body { get; }

    public NarrativeSceneMode Mode { get; }

    public override GameCapability RequiredCapability => NarrativeCapabilities.Scenes;
}
