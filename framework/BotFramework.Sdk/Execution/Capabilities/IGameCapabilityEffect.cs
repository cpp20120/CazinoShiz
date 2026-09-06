namespace BotFramework.Sdk.Execution;

/// <summary>
/// Lets a module-defined custom effect identify the runtime feature needed to
/// apply it. This keeps the effect declarative and free of adapter types.
/// </summary>
public interface IGameCapabilityEffect : IGameEffect
{
    GameCapability RequiredCapability { get; }
}
