namespace BotFramework.Sdk.Execution;

/// <summary>
/// A transport-neutral progression instruction emitted with a game phase rule.
/// The game decides its phase graph; the engine owns token validation and the
/// monotonic round/phase counters.
/// </summary>
public abstract record RoundCompletion<TPhase>
    where TPhase : notnull
{
    private RoundCompletion()
    {
    }

    public abstract RoundCompletionKind Kind { get; }

    internal sealed record ContinuePhase : RoundCompletion<TPhase>
    {
        public override RoundCompletionKind Kind => RoundCompletionKind.Continue;
    }

    internal sealed record CompleteGame : RoundCompletion<TPhase>
    {
        public override RoundCompletionKind Kind => RoundCompletionKind.Complete;
    }

    internal sealed record AdvancePhase(
        TPhase NextPhase,
        DateTimeOffset? NextPhaseDeadline) : RoundCompletion<TPhase>
    {
        public override RoundCompletionKind Kind => RoundCompletionKind.AdvancePhase;
    }

    internal sealed record StartNextRound(
        TPhase FirstPhase,
        DateTimeOffset? FirstPhaseDeadline) : RoundCompletion<TPhase>
    {
        public override RoundCompletionKind Kind => RoundCompletionKind.StartNextRound;
    }

    internal sealed record WaitForInput(InputRequestEffect Input) : RoundCompletion<TPhase>
    {
        public override RoundCompletionKind Kind => RoundCompletionKind.WaitForInput;
    }
}
