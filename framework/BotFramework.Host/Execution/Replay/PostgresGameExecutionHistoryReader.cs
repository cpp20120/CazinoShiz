using System.Text.Json;
using BotFramework.Sdk.Execution;
using Dapper;

namespace BotFramework.Host.Execution;

/// <summary>
/// PostgreSQL reader for execution history. The normal connection tenant scope
/// and RLS policy protect tenant-bound rows; callers remain responsible for
/// administrative authorization before exposing raw snapshots.
/// </summary>
public sealed class PostgresGameExecutionHistoryReader(INpgsqlConnectionFactory connections)
    : IGameExecutionHistoryReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GameExecutionHistoryEntry?> GetAsync(string commandId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        await using var connection = await connections.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT id AS Id,
                   command_id AS CommandId,
                   game_id AS GameId,
                   aggregate_id AS AggregateId,
                   command_type AS CommandType,
                   command_payload::text AS CommandPayload,
                   state_type AS StateType,
                   previous_state::text AS PreviousState,
                   next_state::text AS NextState,
                   result_type AS ResultType,
                   result_payload::text AS ResultPayload,
                   decision_status AS DecisionStatus,
                   rejection_reason AS RejectionReason,
                   entropy::text AS Entropy,
                   effects::text AS Effects,
                   occurred_at AS OccurredAt
            FROM game_execution_history
            WHERE command_id = @commandId
            """,
            new { commandId },
            cancellationToken: ct));
        return row is null ? null : ToEntry(row);
    }

    public async Task<IReadOnlyList<GameExecutionHistoryEntry>> ListAsync(
        string gameId,
        string aggregateId,
        int take = 100,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        if (take is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(take), "Take must be between 1 and 500.");

        await using var connection = await connections.OpenAsync(ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            """
            SELECT id AS Id,
                   command_id AS CommandId,
                   game_id AS GameId,
                   aggregate_id AS AggregateId,
                   command_type AS CommandType,
                   command_payload::text AS CommandPayload,
                   state_type AS StateType,
                   previous_state::text AS PreviousState,
                   next_state::text AS NextState,
                   result_type AS ResultType,
                   result_payload::text AS ResultPayload,
                   decision_status AS DecisionStatus,
                   rejection_reason AS RejectionReason,
                   entropy::text AS Entropy,
                   effects::text AS Effects,
                   occurred_at AS OccurredAt
            FROM game_execution_history
            WHERE game_id = @gameId AND aggregate_id = @aggregateId
            ORDER BY id DESC
            LIMIT @take
            """,
            new { gameId, aggregateId, take },
            cancellationToken: ct));
        return rows.Reverse().Select(ToEntry).ToArray();
    }

    private static GameExecutionHistoryEntry ToEntry(Row row)
    {
        if (!Enum.TryParse<DecisionStatus>(row.DecisionStatus, true, out var status))
            throw new InvalidOperationException($"Unknown replay decision status '{row.DecisionStatus}'.");
        var entropy = JsonSerializer.Deserialize<Dictionary<string, double>>(row.Entropy, JsonOptions)
            ?? new Dictionary<string, double>(StringComparer.Ordinal);
        var effects = JsonSerializer.Deserialize<List<GameReplayEffect>>(row.Effects, JsonOptions) ?? [];
        return new(
            row.Id,
            row.CommandId,
            row.GameId,
            row.AggregateId,
            new(row.CommandType, row.CommandPayload),
            row.PreviousState is null ? null : new(row.StateType, row.PreviousState),
            row.NextState is null ? null : new(row.StateType, row.NextState),
            new(row.ResultType, row.ResultPayload),
            status,
            row.RejectionReason,
            entropy,
            effects,
            new DateTimeOffset(DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)));
    }

    private sealed record Row(
        long Id,
        string CommandId,
        string GameId,
        string AggregateId,
        string CommandType,
        string CommandPayload,
        string StateType,
        string? PreviousState,
        string? NextState,
        string ResultType,
        string ResultPayload,
        string DecisionStatus,
        string? RejectionReason,
        string Entropy,
        string Effects,
        DateTime OccurredAt);
}
