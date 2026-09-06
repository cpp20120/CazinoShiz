namespace BotFramework.Sdk.Execution;

/// <summary>
/// Invariant multiplayer-lobby limits. Game-specific start conditions stay in
/// game rules; this configuration owns only generic capacity and readiness.
/// </summary>
public sealed record LobbyConfiguration
{
    public LobbyConfiguration(
        int minimumPlayers,
        int maximumPlayers,
        bool requireAllPlayersReady = true,
        int? seatCount = null,
        int? maximumSpectators = null)
    {
        if (minimumPlayers < 1)
            throw new ArgumentOutOfRangeException(nameof(minimumPlayers), "Minimum players must be positive.");
        if (maximumPlayers < minimumPlayers)
            throw new ArgumentOutOfRangeException(nameof(maximumPlayers), "Maximum players cannot be lower than minimum players.");
        if (seatCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(seatCount), "Seat count must be positive.");
        if (seatCount is { } seats && seats < maximumPlayers)
            throw new ArgumentOutOfRangeException(nameof(seatCount), "Seat count cannot be lower than maximum players.");
        if (maximumSpectators is < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSpectators), "Maximum spectators cannot be negative.");

        MinimumPlayers = minimumPlayers;
        MaximumPlayers = maximumPlayers;
        RequireAllPlayersReady = requireAllPlayersReady;
        SeatCount = seatCount ?? maximumPlayers;
        MaximumSpectators = maximumSpectators;
    }

    public int MinimumPlayers { get; }

    public int MaximumPlayers { get; }

    public bool RequireAllPlayersReady { get; }

    /// <summary>Number of numbered seats, zero-based in the runtime model.</summary>
    public int SeatCount { get; }

    /// <summary>Null means that spectators are not capacity-limited by the framework.</summary>
    public int? MaximumSpectators { get; }
}
