namespace BotFramework.Sdk.Execution;

/// <summary>
/// Creates token-bound effects for the existing durable schedule outbox. Host
/// scopes the returned id by game and aggregate, so distinct games can reuse it.
/// </summary>
public static class RoundDeadlineSchedule
{
    /// <summary>Stable schedule id for the exact phase occurrence.</summary>
    public static string Id<TPhase>(RoundPhaseToken<TPhase> phase)
        where TPhase : notnull
    {
        ArgumentNullException.ThrowIfNull(phase);
        return $"round-phase:{phase.Round}:{phase.PhaseNumber}";
    }

    /// <summary>
    /// Schedules a one-shot deadline command through the durable schedule
    /// outbox. The command's token, not scheduler timing, decides whether it
    /// can still affect the aggregate.
    /// </summary>
    public static ScheduleEffect Schedule<TPhase>(
        DateTimeOffset dueAt,
        IRoundDeadlineCommand<TPhase> command)
        where TPhase : notnull
    {
        ArgumentNullException.ThrowIfNull(command);
        var phase = command.ExpectedPhase;
        if (phase is null)
            throw new ArgumentException("A round deadline command requires an expected phase.", nameof(command));
        var commandType = command.GetType();
        return ScheduleEffect.Schedule(
            Id(phase),
            AtomicGameSchedule.JobKey(commandType),
            dueAt,
            AtomicGameSchedule.SerializeCommand(command, commandType));
    }

    /// <summary>Cancels the deadline for one exact phase occurrence.</summary>
    public static ScheduleEffect Cancel<TPhase>(RoundPhaseToken<TPhase> phase)
        where TPhase : notnull => ScheduleEffect.Cancel(Id(phase));

    /// <summary>
    /// Cancels a previous phase deadline and schedules the next one in the
    /// same game decision. The old token remains safe even if its job already
    /// fired before the cancellation reached the scheduler.
    /// </summary>
    public static IReadOnlyList<ScheduleEffect> Replace<TPhase>(
        RoundPhaseToken<TPhase> previousPhase,
        DateTimeOffset dueAt,
        IRoundDeadlineCommand<TPhase> command)
        where TPhase : notnull => [Cancel(previousPhase), Schedule(dueAt, command)];
}
