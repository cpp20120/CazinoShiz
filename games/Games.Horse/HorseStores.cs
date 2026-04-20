using BotFramework.Host;
using Dapper;

namespace Games.Horse;

public sealed record HorseBetRow(Guid Id, string RaceDate, long UserId, int HorseId, int Amount);

public sealed record HorseResultRow(string RaceDate, int Winner, string? FileId);

public interface IHorseBetStore
{
    Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAsync(string raceDate, CancellationToken ct);
    Task InsertAsync(HorseBetRow bet, CancellationToken ct);
    Task DeleteByRaceDateAsync(string raceDate, CancellationToken ct);
}

public interface IHorseResultStore
{
    Task<HorseResultRow?> FindAsync(string raceDate, CancellationToken ct);
    Task UpsertAsync(HorseResultRow result, CancellationToken ct);
    Task SaveFileIdAsync(string raceDate, string fileId, CancellationToken ct);
}

public sealed class HorseBetStore(INpgsqlConnectionFactory connections) : IHorseBetStore
{
    public async Task<IReadOnlyList<HorseBetRow>> ListByRaceDateAsync(string raceDate, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<HorseBetRow>(new CommandDefinition(
            "SELECT id AS Id, race_date AS RaceDate, user_id AS UserId, horse_id AS HorseId, amount AS Amount FROM horse_bets WHERE race_date = @raceDate",
            new { raceDate },
            cancellationToken: ct));
        return [.. rows];
    }

    public async Task InsertAsync(HorseBetRow bet, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO horse_bets (id, race_date, user_id, horse_id, amount)
            VALUES (@Id, @RaceDate, @UserId, @HorseId, @Amount)
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
}

public sealed class HorseResultStore(INpgsqlConnectionFactory connections) : IHorseResultStore
{
    public async Task<HorseResultRow?> FindAsync(string raceDate, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<HorseResultRow>(new CommandDefinition(
            "SELECT race_date AS RaceDate, winner AS Winner, file_id AS FileId FROM horse_results WHERE race_date = @raceDate",
            new { raceDate },
            cancellationToken: ct));
    }

    public async Task UpsertAsync(HorseResultRow result, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO horse_results (race_date, winner)
            VALUES (@RaceDate, @Winner)
            ON CONFLICT (race_date) DO UPDATE SET
                winner = EXCLUDED.winner
            """,
            result,
            cancellationToken: ct));
    }

    public async Task SaveFileIdAsync(string raceDate, string fileId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE horse_results SET file_id = @fileId WHERE race_date = @raceDate",
            new { raceDate, fileId },
            cancellationToken: ct));
    }
}
