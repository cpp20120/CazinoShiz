using System.Text.Json;
using BotFramework.Host.Execution;
using Games.Horse.Application.Execution;

namespace Games.Horse.Infrastructure.Persistence;

public sealed class HorseRunStateStore : IGameStateStore<HorseRunCommand, HorseRaceState>
{
    public async Task<HorseRaceState> LoadAsync(
        HorseRunCommand command, IGameExecutionContext context, CancellationToken ct)
    {
        var scopePredicate = command.Kind == HorseRunKind.Global
            ? string.Empty
            : " AND balance_scope_id=@ChatScopeId";
        var json = await context.QuerySingleOrDefaultAsync<string>($"""
            SELECT COALESCE(json_agg(json_build_object(
                'Id',id,'RaceDate',race_date,'UserId',user_id,'BalanceScopeId',balance_scope_id,
                'HorseId',horse_id,'Amount',amount,'WagerBetId',wager_bet_id) ORDER BY id)::text,'[]')
            FROM (SELECT * FROM horse_bets WHERE race_date=@RaceDate{scopePredicate} FOR UPDATE) locked
            """, new { command.RaceDate, command.ChatScopeId }, ct);
        var bets = JsonSerializer.Deserialize<HorseBetRow[]>(json ?? "[]") ?? [];
        if (!bets.Select(bet => bet.Id).Order().SequenceEqual(
                command.ExpectedBets.Select(bet => bet.Id).Order()))
        {
            throw new InvalidOperationException("Horse bets changed before all payout wallets were locked.");
        }
        var winner = await context.QuerySingleOrDefaultAsync<int?>("""
            SELECT winner FROM horse_results
            WHERE race_date=@RaceDate AND balance_scope_id=@ResultScopeId FOR UPDATE
            """, new { command.RaceDate, command.ResultScopeId }, ct);
        return new(bets, [], winner);
    }

    public async Task SaveAsync(
        HorseRunCommand command, HorseRaceState state, IGameExecutionContext context, CancellationToken ct)
    {
        var winner = state.Winner ?? throw new InvalidOperationException("Accepted horse race has no winner.");
        foreach (var scopeId in state.ResultScopes)
        {
            await context.ExecuteAsync("""
                INSERT INTO horse_results (race_date,balance_scope_id,winner)
                VALUES (@RaceDate,@scopeId,@winner)
                ON CONFLICT (race_date,balance_scope_id) DO UPDATE SET winner=EXCLUDED.winner
                """, new { command.RaceDate, scopeId, winner }, ct);
        }
        var deleted = await context.ExecuteAsync(
            command.Kind == HorseRunKind.Global
                ? "DELETE FROM horse_bets WHERE race_date=@RaceDate"
                : "DELETE FROM horse_bets WHERE race_date=@RaceDate AND balance_scope_id=@ChatScopeId",
            new { command.RaceDate, command.ChatScopeId }, ct);
        if (deleted != state.Bets.Count)
            throw new InvalidOperationException("Horse bet set changed before settlement.");
    }
}
