using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.DeferredOutcome;

namespace BotFramework.Host.Execution.DeferredOutcome;

/// <summary>
/// Standard descriptor for all three commands of a deferred-outcome wager.
/// It scopes state by user/chat and opts into a daily quota only when the model
/// definition declares one.
/// </summary>
public abstract class DeferredOutcomeWagerDescriptor<TGame, TOutcome, TCommand, TResult>(
    DeferredOutcomeWagerDefinition<TGame, TOutcome> definition,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : DailyGameQuotaExecutionDescriptor<TCommand, DeferredOutcomeWagerState, TResult>(definition.Game, dailyQuotaPolicy)
    where TGame : IDeferredOutcomeWagerGame
    where TCommand : IDeferredOutcomeWagerCommand
{
    public override DeferredOutcomeWagerState CreateInitialState(TCommand command) =>
        DeferredOutcomeWagerState.Empty;

    protected override string PlayerAggregateId(TCommand command) => $"{command.ChatId}:{command.UserId}";

    protected override string? DailyQuotaId(TCommand command) => definition.Game.DailyQuotaId;
}
