namespace BotFramework.Sdk.Execution;

#pragma warning disable MA0048 // TurnCompletion<TPlayerId> occupies the conventional file name.

/// <summary>Factories for framework-owned turn progression instructions.</summary>
public static class TurnCompletion
{
    /// <summary>Keeps the current player and current deadline.</summary>
    public static TurnCompletion<TPlayerId> Continue<TPlayerId>()
        where TPlayerId : notnull => new TurnCompletion<TPlayerId>.ContinueTurn();

    /// <summary>Ends the game and clears its current turn.</summary>
    public static TurnCompletion<TPlayerId> Complete<TPlayerId>()
        where TPlayerId : notnull => new TurnCompletion<TPlayerId>.CompleteTurn();

    /// <summary>Moves the next turn to an admitted player.</summary>
    public static TurnCompletion<TPlayerId> PassTo<TPlayerId>(
        TPlayerId playerId,
        DateTimeOffset? nextTurnDeadline = null)
        where TPlayerId : notnull
    {
        ArgumentNullException.ThrowIfNull(playerId);
        return new TurnCompletion<TPlayerId>.PassToTurn(playerId, nextTurnDeadline);
    }

    /// <summary>
    /// Keeps the current player and waits for a durable generic input request.
    /// The request is automatically emitted as a custom game effect.
    /// </summary>
    public static TurnCompletion<TPlayerId> WaitFor<TPlayerId>(InputRequestEffect input)
        where TPlayerId : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        return new TurnCompletion<TPlayerId>.WaitForInputTurn(input);
    }
}

#pragma warning restore MA0048
