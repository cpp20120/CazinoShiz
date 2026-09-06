namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>
/// A marker type identifies one independently registered deferred-outcome game.
/// It lets a host register several games with the same outcome type safely.
/// </summary>
public interface IDeferredOutcomeWagerGame;
