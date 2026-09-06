using System.Text.Json.Serialization;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Framework-owned portion of a phased aggregate. A game embeds it in its own
/// state and remains responsible for phase order, board data, scoring and
/// terminal outcome semantics.
/// </summary>
public sealed record RoundEngineState<TPhase>
    where TPhase : notnull
{
    public RoundEngineState(
        RoundGameStatus status,
        long round,
        long phaseNumber,
        TPhase phase,
        DateTimeOffset? phaseDeadline)
    {
        if (round < 0)
            throw new ArgumentOutOfRangeException(nameof(round), "Round cannot be negative.");
        if (phaseNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(phaseNumber), "Phase number cannot be negative.");
        ArgumentNullException.ThrowIfNull(phase);
        EnsureValidShape(status, round, phaseNumber, phaseDeadline);

        Status = status;
        Round = round;
        PhaseNumber = phaseNumber;
        Phase = phase;
        PhaseDeadline = phaseDeadline;
    }

    public RoundGameStatus Status { get; }

    /// <summary>One-based while active/suspended; zero before the first phase.</summary>
    public long Round { get; }

    /// <summary>Monotonic phase sequence; zero before the first phase.</summary>
    public long PhaseNumber { get; }

    /// <summary>
    /// Current phase while active or suspended. Waiting state stores the first
    /// phase to enter; terminal states retain the last phase for diagnostics.
    /// </summary>
    public TPhase Phase { get; }

    /// <summary>Absolute deadline of the active phase; null means no deadline.</summary>
    public DateTimeOffset? PhaseDeadline { get; }

    [JsonIgnore]
    public bool HasActivePhase => Status is RoundGameStatus.Active or RoundGameStatus.Suspended;

    [JsonIgnore]
    public RoundPhaseToken<TPhase> CurrentPhase => HasActivePhase
        ? new(Round, PhaseNumber, Phase)
        : throw new InvalidOperationException("The round state has no active phase.");

    private static void EnsureValidShape(
        RoundGameStatus status,
        long round,
        long phaseNumber,
        DateTimeOffset? phaseDeadline)
    {
        if (status == RoundGameStatus.WaitingToStart)
        {
            if (round != 0 || phaseNumber != 0)
                throw new ArgumentException("A waiting game has not started a round or phase.", nameof(status));
            if (phaseDeadline is not null)
                throw new ArgumentException("A waiting game cannot retain an active deadline.", nameof(phaseDeadline));
            return;
        }

        if (status is RoundGameStatus.Completed or RoundGameStatus.Aborted)
        {
            if (phaseDeadline is not null)
                throw new ArgumentException("A terminal game cannot retain an active deadline.", nameof(phaseDeadline));
            return;
        }

        if (round < 1 || phaseNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(round), "An active or suspended game requires positive round and phase numbers.");
        if (status != RoundGameStatus.Active && phaseDeadline is not null)
            throw new ArgumentException("Only an active phase can retain an active deadline.", nameof(phaseDeadline));
    }
}
