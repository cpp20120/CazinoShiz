using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerAbortDescriptor<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : DeferredOutcomeWagerDescriptor<
        TGame,
        TOutcome,
        DeferredOutcomeWagerAbortCommand<TGame, TOutcome>,
        DeferredOutcomeWagerAbortResult>(definition, dailyQuotaPolicy)
    where TGame : IDeferredOutcomeWagerGame;
