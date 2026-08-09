using BotFramework.Host.Execution;
using Games.Horse.Application.Execution;
using Games.Horse.Application.Wagering;

namespace Games.Horse.Infrastructure.Persistence;

public sealed class HorseWagerStateStore : IGameStateStore<HorsePlaceBetCommand, HorseWagerState>
{
    public async Task<HorseWagerState> LoadAsync(
        HorsePlaceBetCommand command, IGameExecutionContext context, CancellationToken ct)
    {
        var row = await context.QuerySingleOrDefaultAsync<HorseBetRow>("""
            SELECT id AS Id,race_date AS RaceDate,user_id AS UserId,balance_scope_id AS BalanceScopeId,
                   horse_id AS HorseId,amount AS Amount,wager_bet_id AS WagerBetId
            FROM horse_bets WHERE id=@BetId FOR UPDATE
            """, new { command.BetId }, ct);
        return row is null
            ? new(null, null, command.UserId, command.BalanceScopeId, command.HorseId)
            : new(row.Id, row.RaceDate, row.UserId, row.BalanceScopeId, row.HorseId + 1);
    }

    public async Task SaveAsync(
        HorsePlaceBetCommand command, HorseWagerState state, IGameExecutionContext context, CancellationToken ct)
    {
        if (state.BetId is null) throw new InvalidOperationException("Accepted horse wager is missing.");
        var inserted = await context.ExecuteAsync("""
            INSERT INTO horse_bets (id,race_date,user_id,balance_scope_id,horse_id,amount,wager_bet_id)
            VALUES (@BetId,@RaceDate,@UserId,@BalanceScopeId,@HorseId,@Amount,@WagerBetId)
            ON CONFLICT (id) DO NOTHING
            """, new
        {
            BetId = state.BetId,
            state.RaceDate,
            state.PlayerId,
            state.BalanceScopeId,
            HorseId = state.HorseId - 1,
            Amount = command.Amount,
            command.WagerBetId,
        }, ct);
        if (inserted != 1) throw new InvalidOperationException("Horse wager bet already exists.");
    }
}
