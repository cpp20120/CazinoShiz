using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

/// <summary>Validates that a declared game feature exists in this runtime and covers each emitted effect.</summary>
public interface IGameCapabilityValidator
{
    void Validate(GameCapabilitySet requiredCapabilities, GameEffectSet effects);
}
