using Dapper;

namespace Games.Horse.Infrastructure.Persistence;

public sealed class HorseBetStore(INpgsqlConnectionFactory connections) : IHorseBetStore
{
    public async Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAsync(string raceDate, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<HorseBetRow>(new CommandDefinition(
            """
            SELECT id AS Id, race_date AS RaceDate, user_id AS UserId, balance_scope_id AS BalanceScopeId,
                   horse_id AS HorseId, amount AS Amount, wager_bet_id AS WagerBetId
            FROM horse_bets WHERE race_date = @raceDate
            """,
            new { raceDate },
            cancellationToken: ct));
        return [.. rows];
    }

    public async Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAndScopeAsync(
        string raceDate, long balanceScopeId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<HorseBetRow>(new CommandDefinition(
            """
            SELECT id AS Id, race_date AS RaceDate, user_id AS UserId, balance_scope_id AS BalanceScopeId,
                   horse_id AS HorseId, amount AS Amount, wager_bet_id AS WagerBetId
            FROM horse_bets
            WHERE race_date = @raceDate AND balance_scope_id = @balanceScopeId
            """,
            new { raceDate, balanceScopeId },
            cancellationToken: ct));
        return [.. rows];
    }

    public async Task InsertAsync(HorseBetRow bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO horse_bets (id, race_date, user_id, balance_scope_id, horse_id, amount, wager_bet_id)
            VALUES (@Id, @RaceDate, @UserId, @BalanceScopeId, @HorseId, @Amount, @WagerBetId)
            ON CONFLICT (id) DO NOTHING
            """,
            bet,
            cancellationToken: ct));
    }

    public async Task DeleteByRaceDateAsync(string raceDate, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM horse_bets WHERE race_date = @raceDate",
            new { raceDate },
            cancellationToken: ct));
    }

    public async Task DeleteByRaceDateAndScopeAsync(string raceDate, long balanceScopeId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM horse_bets
            WHERE race_date = @raceDate AND balance_scope_id = @balanceScopeId
            """,
            new { raceDate, balanceScopeId },
            cancellationToken: ct));
    }
}
