using System.Text.Json;
using BotFramework.Host.Execution;
using Games.Horse.Application.Execution;

namespace Games.Horse.Infrastructure.Persistence;

public sealed class HorsePlaceBetStateStore : IGameStateStore<HorsePlaceBetCommand, HorseBetState>
{
    public async Task<HorseBetState> LoadAsync(
        HorsePlaceBetCommand command, IGameExecutionContext context, CancellationToken ct) =>
        new(await context.QuerySingleOrDefaultAsync<HorseBetRow>("""
            SELECT id AS Id,race_date AS RaceDate,user_id AS UserId,balance_scope_id AS BalanceScopeId,
                   horse_id AS HorseId,amount AS Amount,wager_bet_id AS WagerBetId
            FROM horse_bets WHERE id=@BetId FOR UPDATE
            """, new { command.BetId }, ct));

    public async Task SaveAsync(
        HorsePlaceBetCommand command, HorseBetState state, IGameExecutionContext context, CancellationToken ct)
    {
        var bet = state.Bet ?? throw new InvalidOperationException("Accepted horse bet is missing.");
        var inserted = await context.ExecuteAsync("""
            INSERT INTO horse_bets (id,race_date,user_id,balance_scope_id,horse_id,amount)
            VALUES (@Id,@RaceDate,@UserId,@BalanceScopeId,@HorseId,@Amount)
            ON CONFLICT (id) DO NOTHING
            """, bet, ct);
        if (inserted != 1) throw new InvalidOperationException("Horse bet already exists.");
    }
}
