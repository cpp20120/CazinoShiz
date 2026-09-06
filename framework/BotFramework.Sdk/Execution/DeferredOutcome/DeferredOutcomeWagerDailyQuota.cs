namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>Enables the standard per-player, per-game daily quota for a wager model.</summary>
public sealed record DeferredOutcomeWagerDailyQuota
{
    public DeferredOutcomeWagerDailyQuota(string id)
    {
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Quota id is required.", nameof(id))
            : id;
    }

    public string Id { get; }
}
