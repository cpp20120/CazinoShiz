using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerGameExecutor<TGame, TOutcome>(
    IAtomicGameExecutor<
        DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerPlaceResult> place,
    IAtomicGameExecutor<
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerResolveResult<TOutcome>> resolve,
    IAtomicGameExecutor<
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome>,
        DeferredOutcomeWagerState,
        DeferredOutcomeWagerAbortResult> abort)
    : IDeferredOutcomeWagerGameExecutor<TGame, TOutcome>
    where TGame : IDeferredOutcomeWagerGame
{
    public Task<DeferredOutcomeWagerPlaceResult> PlaceAsync(
        DeferredOutcomeWagerPlaceCommand<TGame, TOutcome> command,
        CancellationToken ct) =>
        place.ExecuteAsync(new GameExecutionEnvelope<DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>>(command), ct);

    public Task<DeferredOutcomeWagerResolveResult<TOutcome>> ResolveAsync(
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome> command,
        CancellationToken ct) =>
        resolve.ExecuteAsync(new GameExecutionEnvelope<DeferredOutcomeWagerResolveCommand<TGame, TOutcome>>(command), ct);

    public Task<DeferredOutcomeWagerAbortResult> AbortAsync(
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome> command,
        CancellationToken ct) =>
        abort.ExecuteAsync(new GameExecutionEnvelope<DeferredOutcomeWagerAbortCommand<TGame, TOutcome>>(command), ct);
}
