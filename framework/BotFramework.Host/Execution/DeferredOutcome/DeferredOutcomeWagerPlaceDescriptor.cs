using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

public sealed class DeferredOutcomeWagerPlaceDescriptor<TGame, TOutcome>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : DeferredOutcomeWagerDescriptor<
        TGame,
        TOutcome,
        DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>,
        DeferredOutcomeWagerPlaceResult>(definition, dailyQuotaPolicy)
    where TGame : IDeferredOutcomeWagerGame;
