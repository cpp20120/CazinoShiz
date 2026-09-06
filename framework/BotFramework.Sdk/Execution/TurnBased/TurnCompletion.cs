namespace BotFramework.Sdk.Execution;

/// <summary>
/// A transport-neutral instruction for the turn state machine emitted together
/// with a game rule result. Use the factory methods instead of inventing
/// transport or UI states in the game aggregate.
/// </summary>
public abstract record TurnCompletion<TPlayerId>
    where TPlayerId : notnull
{
    private TurnCompletion()
    {
    }

    public abstract TurnCompletionKind Kind { get; }

    internal sealed record ContinueTurn : TurnCompletion<TPlayerId>
    {
        public override TurnCompletionKind Kind => TurnCompletionKind.Continue;
    }

    internal sealed record CompleteTurn : TurnCompletion<TPlayerId>
    {
        public override TurnCompletionKind Kind => TurnCompletionKind.Complete;
    }

    internal sealed record PassToTurn(
        TPlayerId PlayerId,
        DateTimeOffset? NextTurnDeadline) : TurnCompletion<TPlayerId>
    {
        public override TurnCompletionKind Kind => TurnCompletionKind.PassTo;
    }

    internal sealed record WaitForInputTurn(InputRequestEffect Input) : TurnCompletion<TPlayerId>
    {
        public override TurnCompletionKind Kind => TurnCompletionKind.WaitForInput;
    }
}
