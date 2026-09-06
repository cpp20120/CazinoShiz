namespace BotFramework.Sdk.Execution;

/// <summary>Maps built-in effects to the runtime features needed to apply them.</summary>
public static class GameEffectCapabilityResolver
{
    public static GameCapability? GetRequiredCapability(IGameEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        return effect switch
        {
            EconomyEffect or WalletEconomyEffect or TenantWalletEconomyEffect => EconomyCapabilities.Wallet,
            QuotaEffect => EconomyCapabilities.Quota,
            IGameRecord => PersistenceCapabilities.Records,
            ScheduleEffect => SchedulingCapabilities.Timers,
            IGameCapabilityEffect capabilityEffect => capabilityEffect.RequiredCapability,
            _ => null,
        };
    }

    public static IReadOnlyList<GameCapability> GetRequiredCapabilities(GameEffectSet effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var capabilities = new Dictionary<string, GameCapability>(StringComparer.Ordinal);
        foreach (var effect in effects.MaterializeEffects())
            Add(GetRequiredCapability(effect), capabilities);
        if (effects.Events.Count != 0)
            Add(PersistenceCapabilities.Events, capabilities);
        return capabilities.Values.ToArray();
    }

    public static IReadOnlyList<IGameEffect> GetUnboundCustomEffects(GameEffectSet effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return effects.Custom
            .Where(effect => GetRequiredCapability(effect) is null)
            .ToArray();
    }

    private static void Add(GameCapability? capability, IDictionary<string, GameCapability> capabilities)
    {
        if (capability is not null)
            capabilities.TryAdd(capability.Id, capability);
    }
}
