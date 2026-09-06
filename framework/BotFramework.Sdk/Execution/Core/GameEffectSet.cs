using BotFramework.Sdk.Events.Contracts;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// A fully materialized set of effects and domain events emitted by one
/// decision. Domain events are retained as a separate category because they
/// are persisted/dispatched through the event pipeline, not handled as custom
/// game effects.
/// </summary>
public sealed record GameEffectSet(
    IReadOnlyList<EconomyEffect> Economy,
    IReadOnlyList<QuotaEffect> Quotas,
    IReadOnlyList<IGameRecord> Records,
    IReadOnlyList<IGameEffect> Custom,
    IReadOnlyList<IDomainEvent> Events,
    IReadOnlyList<ScheduleEffect> Schedules)
{
    /// <summary>A reusable empty effect set for decisions with no side effects.</summary>
    public static GameEffectSet Empty { get; } = new([], [], [], [], [], []);

    public int Count => Economy.Count + Quotas.Count + Records.Count + Custom.Count + Events.Count + Schedules.Count;

    /// <summary>Returns a copy that includes one additional custom effect.</summary>
    public GameEffectSet WithCustom(IGameEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return Custom.Contains(effect)
            ? this
            : this with { Custom = [.. Custom, effect] };
    }

    public IReadOnlyList<IGameEffect> MaterializeEffects()
    {
        var effectCount = Economy.Count + Quotas.Count + Records.Count + Custom.Count + Schedules.Count;
        if (effectCount == 0) return [];

        var effects = new List<IGameEffect>(effectCount);
        effects.AddRange(Economy);
        effects.AddRange(Quotas);
        effects.AddRange(Records);
        effects.AddRange(Custom);
        effects.AddRange(Schedules);
        return effects;
    }
}
