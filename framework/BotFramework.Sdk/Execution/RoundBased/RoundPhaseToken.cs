namespace BotFramework.Sdk.Execution;

/// <summary>
/// Identifies one exact active phase. Persist it in player input and scheduled
/// deadline commands so a late delivery cannot mutate a later occurrence of
/// the same logical phase.
/// </summary>
public sealed record RoundPhaseToken<TPhase>
    where TPhase : notnull
{
    public RoundPhaseToken(long round, long phaseNumber, TPhase phase)
    {
        if (round < 1)
            throw new ArgumentOutOfRangeException(nameof(round), "Round must be positive.");
        if (phaseNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(phaseNumber), "Phase number must be positive.");
        ArgumentNullException.ThrowIfNull(phase);

        Round = round;
        PhaseNumber = phaseNumber;
        Phase = phase;
    }

    /// <summary>One-based logical round number.</summary>
    public long Round { get; }

    /// <summary>Monotonic phase sequence number across the complete match.</summary>
    public long PhaseNumber { get; }

    /// <summary>Game-defined phase identifier.</summary>
    public TPhase Phase { get; }
}
