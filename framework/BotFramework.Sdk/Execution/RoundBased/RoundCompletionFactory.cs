namespace BotFramework.Sdk.Execution;

#pragma warning disable MA0048 // RoundCompletion<TPhase> occupies the conventional file name.

/// <summary>Factories for framework-owned round and phase progression.</summary>
public static class RoundCompletion
{
    /// <summary>Keeps the current phase and its deadline.</summary>
    public static RoundCompletion<TPhase> Continue<TPhase>()
        where TPhase : notnull => new RoundCompletion<TPhase>.ContinuePhase();

    /// <summary>Ends the game and clears the current phase deadline.</summary>
    public static RoundCompletion<TPhase> Complete<TPhase>()
        where TPhase : notnull => new RoundCompletion<TPhase>.CompleteGame();

    /// <summary>Enters a game-defined next phase in the current round.</summary>
    public static RoundCompletion<TPhase> AdvanceTo<TPhase>(
        TPhase nextPhase,
        DateTimeOffset? nextPhaseDeadline = null)
        where TPhase : notnull
    {
        ArgumentNullException.ThrowIfNull(nextPhase);
        return new RoundCompletion<TPhase>.AdvancePhase(nextPhase, nextPhaseDeadline);
    }

    /// <summary>Starts a new round at its game-defined first phase.</summary>
    public static RoundCompletion<TPhase> NextRound<TPhase>(
        TPhase firstPhase,
        DateTimeOffset? firstPhaseDeadline = null)
        where TPhase : notnull
    {
        ArgumentNullException.ThrowIfNull(firstPhase);
        return new RoundCompletion<TPhase>.StartNextRound(firstPhase, firstPhaseDeadline);
    }

    /// <summary>
    /// Keeps the current phase and waits for a durable generic input request.
    /// The input expiry becomes the phase deadline and is emitted automatically.
    /// </summary>
    public static RoundCompletion<TPhase> WaitFor<TPhase>(InputRequestEffect input)
        where TPhase : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        return new RoundCompletion<TPhase>.WaitForInput(input);
    }
}

#pragma warning restore MA0048
