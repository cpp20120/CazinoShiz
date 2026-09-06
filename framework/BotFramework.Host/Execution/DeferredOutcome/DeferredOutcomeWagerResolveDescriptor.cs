using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerResolveDescriptor<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : DeferredOutcomeWagerDescriptor<
        TGame,
        TOutcome,
        DeferredOutcomeWagerResolveCommand<TGame, TOutcome>,
        DeferredOutcomeWagerResolveResult<TOutcome>>(definition, dailyQuotaPolicy)
    where TGame : IDeferredOutcomeWagerGame;
