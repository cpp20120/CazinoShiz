namespace BotFramework.Sdk.Execution;

/// <summary>
/// Contract for a game command caused by one exact phase deadline. The command
/// still enters the ordinary atomic executor, where it must call
/// <see cref="RoundEngine{TPhase}.ApplyTimeout{TState}"/> or
/// <see cref="RoundEngine{TPhase}.Timeout"/> before changing game state.
/// </summary>
public interface IRoundDeadlineCommand<TPhase>
    where TPhase : notnull
{
    RoundPhaseToken<TPhase> ExpectedPhase { get; }
}
