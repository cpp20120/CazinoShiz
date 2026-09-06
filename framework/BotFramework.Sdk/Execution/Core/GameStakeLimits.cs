namespace BotFramework.Sdk.Execution;

/// <summary>Inclusive stake bounds for a wager-capable game.</summary>
public sealed record GameStakeLimits
{
    public GameStakeLimits(long minimumAmount, long? maximumAmount = null)
    {
        if (minimumAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumAmount), "Minimum stake must be positive.");
        if (maximumAmount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAmount), "Maximum stake must be positive.");
        if (maximumAmount is { } maximum && maximum < minimumAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAmount),
                "Maximum stake cannot be smaller than minimum stake.");
        }

        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
    }

    public long MinimumAmount { get; }
    public long? MaximumAmount { get; }

    public bool Allows(long amount) => amount >= MinimumAmount
        && (MaximumAmount is null || amount <= MaximumAmount);
}
