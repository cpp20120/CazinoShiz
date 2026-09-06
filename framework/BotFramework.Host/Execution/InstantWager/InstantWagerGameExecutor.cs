using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.InstantWager;

namespace BotFramework.Host.Execution.InstantWager;

public sealed class InstantWagerGameExecutor<TGame, TOutcome>(
    IAtomicGameExecutor<InstantWagerCommand<TGame>, NoGameState, InstantWagerResult<TOutcome>> executor)
    : IInstantWagerGameExecutor<TGame, TOutcome>
    where TGame : IInstantWagerGame
{
    public Task<InstantWagerResult<TOutcome>> ExecuteAsync(InstantWagerCommand<TGame> command, CancellationToken ct) =>
        executor.ExecuteAsync(new GameExecutionEnvelope<InstantWagerCommand<TGame>>(command), ct);
}
