namespace Games.Darts;

public sealed class DartsOptions
{
    public const string SectionName = "Games:darts";
    public int MaxBet { get; init; } = 10_000;

    /// <summary>Used when user sends <c>/darts</c> or <c>/darts bet</c> without an amount.</summary>
    public int DefaultBet { get; init; } = 10;
}
