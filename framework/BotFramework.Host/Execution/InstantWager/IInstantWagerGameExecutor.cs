using BotFramework.Sdk.Execution.InstantWager;

namespace BotFramework.Host.Execution.InstantWager;

/// <summary>Application-facing executor for one instant-wager game.</summary>
public interface IInstantWagerGameExecutor<TGame, TOutcome>
    where TGame : IInstantWagerGame
{
    Task<InstantWagerResult<TOutcome>> ExecuteAsync(InstantWagerCommand<TGame> command, CancellationToken ct);
}
