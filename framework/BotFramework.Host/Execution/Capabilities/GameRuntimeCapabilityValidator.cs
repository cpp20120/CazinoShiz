using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Execution;

internal sealed class GameRuntimeCapabilityValidator(
    IEnumerable<IGameRuntimeCapabilityProvider> providers) : IGameCapabilityValidator
{
    private readonly GameCapabilitySet availableCapabilities = new(
        providers.SelectMany(provider => provider.Capabilities.Values));

    public void Validate(GameCapabilitySet requiredCapabilities, GameEffectSet effects)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ArgumentNullException.ThrowIfNull(effects);
        if (requiredCapabilities.IsEmpty)
            return;

        var unsupported = requiredCapabilities.MissingFrom(availableCapabilities);
        if (unsupported.Count != 0)
        {
            throw new InvalidOperationException(
                $"The current runtime does not provide required game capability '{unsupported[0].Id}'.");
        }

        var unboundCustom = GameEffectCapabilityResolver.GetUnboundCustomEffects(effects);
        if (unboundCustom.Count != 0)
        {
            throw new InvalidOperationException(
                $"Custom game effect '{unboundCustom[0].GetType().FullName}' must implement " +
                $"{nameof(IGameCapabilityEffect)} when capability validation is enabled.");
        }

        var undeclared = GameEffectCapabilityResolver.GetRequiredCapabilities(effects)
            .FirstOrDefault(capability => !requiredCapabilities.Contains(capability));
        if (undeclared is not null)
        {
            throw new InvalidOperationException(
                $"The game emitted an effect requiring undeclared capability '{undeclared.Id}'.");
        }
    }
}
