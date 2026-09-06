namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>
/// Common identity required by all commands in a deferred-outcome wager.
/// The outcome itself is supplied later by an external system or transport.
/// </summary>
public interface IDeferredOutcomeWagerCommand : IPlayerGameCommand;
