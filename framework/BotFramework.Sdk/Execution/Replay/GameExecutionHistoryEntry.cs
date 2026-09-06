using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Immutable, ordered journal entry for one committed game command. The
/// snapshots and typed payloads are intended for internal debugging, recovery
/// and audit readers; a public frontend should project its own safe view.
/// </summary>
public sealed record GameExecutionHistoryEntry
{
    public GameExecutionHistoryEntry(
        long id,
        string commandId,
        string gameId,
        string aggregateId,
        GameReplayPayload command,
        GameReplayPayload? previousState,
        GameReplayPayload? nextState,
        GameReplayPayload result,
        DecisionStatus decisionStatus,
        string? rejectionReason,
        IReadOnlyDictionary<string, double> entropy,
        IEnumerable<GameReplayEffect> effects,
        DateTimeOffset occurredAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        CommandId = Required(commandId, nameof(commandId));
        GameId = Required(gameId, nameof(gameId));
        AggregateId = Required(aggregateId, nameof(aggregateId));
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(entropy);
        ArgumentNullException.ThrowIfNull(effects);

        var entropyCopy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in entropy)
        {
            if (string.IsNullOrWhiteSpace(name) || value is < 0 or >= 1 || double.IsNaN(value))
                throw new ArgumentException("Replay entropy must contain named values in [0, 1).", nameof(entropy));
            if (!entropyCopy.TryAdd(name, value))
                throw new ArgumentException("Replay entropy names must be unique.", nameof(entropy));
        }
        var effectCopy = effects.ToArray();
        if (effectCopy.Any(static effect => effect is null))
            throw new ArgumentException("Replay effects cannot contain null.", nameof(effects));

        Id = id;
        Command = command;
        PreviousState = previousState;
        NextState = nextState;
        Result = result;
        DecisionStatus = decisionStatus;
        RejectionReason = rejectionReason;
        Entropy = new ReadOnlyDictionary<string, double>(entropyCopy);
        Effects = new ReadOnlyCollection<GameReplayEffect>(effectCopy);
        OccurredAt = occurredAt;
    }

    public long Id { get; }

    public string CommandId { get; }

    public string GameId { get; }

    public string AggregateId { get; }

    public GameReplayPayload Command { get; }

    public GameReplayPayload? PreviousState { get; }

    public GameReplayPayload? NextState { get; }

    public GameReplayPayload Result { get; }

    public DecisionStatus DecisionStatus { get; }

    public string? RejectionReason { get; }

    public IReadOnlyDictionary<string, double> Entropy { get; }

    public IReadOnlyList<GameReplayEffect> Effects { get; }

    public DateTimeOffset OccurredAt { get; }

    private static string Required(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;
}
