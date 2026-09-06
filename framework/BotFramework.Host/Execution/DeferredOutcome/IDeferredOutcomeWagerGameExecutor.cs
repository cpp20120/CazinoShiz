using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

/// <summary>One dependency for an application service using the deferred-outcome model.</summary>
public interface IDeferredOutcomeWagerGameExecutor<TGame, TOutcome>
    where TGame : IDeferredOutcomeWagerGame
{
    Task<DeferredOutcomeWagerPlaceResult> PlaceAsync(
        DeferredOutcomeWagerPlaceCommand<TGame, TOutcome> command,
        CancellationToken ct);

    Task<DeferredOutcomeWagerResolveResult<TOutcome>> ResolveAsync(
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome> command,
        CancellationToken ct);

    Task<DeferredOutcomeWagerAbortResult> AbortAsync(
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome> command,
        CancellationToken ct);
}
