using BotFramework.Sdk.Execution;
using BotFramework.Sdk.Execution.InstantWager;

namespace BotFramework.Host.Execution.InstantWager;

/// <summary>Framework descriptor for a stateless wager settled in one atomic command.</summary>
public sealed class InstantWagerDescriptor<TGame, TOutcome>(
    InstantWagerDefinition<TGame, TOutcome> definition,
    IGameDailyQuotaPolicy dailyQuotaPolicy)
    : DailyGameQuotaExecutionDescriptor<
        InstantWagerCommand<TGame>,
        NoGameState,
        InstantWagerResult<TOutcome>>(definition.Game, dailyQuotaPolicy)
    where TGame : IInstantWagerGame
{
    protected override string? DailyQuotaId(InstantWagerCommand<TGame> command) =>
        definition.Game.DailyQuotaId;
}
