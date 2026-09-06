using System.Text.Json.Serialization;

namespace BotFramework.Sdk.Execution;

/// <summary>
/// Framework-owned, transport-neutral portion of a turn-driven aggregate.
/// A game usually embeds this in its own versioned state together with rules,
/// board data, scores and domain-specific terminal conditions.
/// </summary>
public sealed record TurnEngineState<TPlayerId>
    where TPlayerId : notnull
{
    public TurnEngineState(
        TurnGameStatus status,
        IReadOnlyList<TPlayerId> playerOrder,
        int currentPlayerIndex,
        long turnNumber,
        long round,
        DateTimeOffset? turnDeadline)
    {
        ArgumentNullException.ThrowIfNull(playerOrder);
        if (turnNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(turnNumber), "Turn number cannot be negative.");
        if (round < 0)
            throw new ArgumentOutOfRangeException(nameof(round), "Round cannot be negative.");

        var players = playerOrder.ToArray();
        EnsureUnique(players);
        EnsureValidShape(status, players.Length, currentPlayerIndex, turnNumber, round, turnDeadline);

        Status = status;
        PlayerOrder = Array.AsReadOnly(players);
        CurrentPlayerIndex = currentPlayerIndex;
        TurnNumber = turnNumber;
        Round = round;
        TurnDeadline = turnDeadline;
    }

    public TurnGameStatus Status { get; }

    /// <summary>Stable cyclic player order, copied on construction.</summary>
    public IReadOnlyList<TPlayerId> PlayerOrder { get; }

    /// <summary>-1 when the game has no active turn.</summary>
    public int CurrentPlayerIndex { get; }

    /// <summary>Monotonic sequence number, starting at one for the first turn.</summary>
    public long TurnNumber { get; }

    /// <summary>One-based cyclic pass over the player order, starting at one.</summary>
    public long Round { get; }

    /// <summary>Absolute deadline of the active turn; null means an untimed turn.</summary>
    public DateTimeOffset? TurnDeadline { get; }

    [JsonIgnore]
    public bool HasCurrentTurn => Status is TurnGameStatus.Active or TurnGameStatus.Suspended;

    [JsonIgnore]
    public TPlayerId CurrentPlayerId => HasCurrentTurn
        ? PlayerOrder[CurrentPlayerIndex]
        : throw new InvalidOperationException("The turn state has no current player.");

    [JsonIgnore]
    public TurnToken<TPlayerId> CurrentTurn => HasCurrentTurn
        ? new(TurnNumber, CurrentPlayerId)
        : throw new InvalidOperationException("The turn state has no current turn.");

    private static void EnsureUnique(TPlayerId[] players)
    {
        if (players.Distinct().Count() != players.Length)
            throw new ArgumentException("Player order cannot contain duplicate player ids.", nameof(players));
    }

    private static void EnsureValidShape(
        TurnGameStatus status,
        int playerCount,
        int currentPlayerIndex,
        long turnNumber,
        long round,
        DateTimeOffset? turnDeadline)
    {
        if (status is TurnGameStatus.Active or TurnGameStatus.Suspended)
        {
            if (playerCount == 0)
                throw new ArgumentException("An active or suspended game requires a player.", nameof(playerCount));
            if (currentPlayerIndex < 0 || currentPlayerIndex >= playerCount)
                throw new ArgumentOutOfRangeException(nameof(currentPlayerIndex));
            if (turnNumber < 1 || round < 1)
                throw new ArgumentOutOfRangeException(nameof(turnNumber), "An active turn must have positive turn and round numbers.");
            if (status == TurnGameStatus.Suspended && turnDeadline is not null)
                throw new ArgumentException("A suspended game cannot retain an active deadline.", nameof(turnDeadline));
            return;
        }

        if (currentPlayerIndex != -1)
            throw new ArgumentOutOfRangeException(nameof(currentPlayerIndex), "An inactive game cannot have a current player.");
        if (turnDeadline is not null)
            throw new ArgumentException("An inactive game cannot have an active deadline.", nameof(turnDeadline));
        if (status != TurnGameStatus.WaitingForPlayers)
            return;
        if (turnNumber != 0)
            throw new ArgumentException("A waiting game has not started a turn.", nameof(turnNumber));
        if (round != 0)
            throw new ArgumentException("A waiting game has not started a round.", nameof(round));
    }
}
