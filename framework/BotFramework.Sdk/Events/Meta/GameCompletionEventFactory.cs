using BotFramework.Sdk.Execution;

namespace BotFramework.Sdk.Events.Meta;

/// <summary>
/// Data needed to create the normalized completion event. It deliberately has
/// no transport fields, so all adapters publish the same game outcome shape.
/// </summary>
public sealed record GameCompletion
{
    public GameCompletion(
        GameCommandContext player,
        string gameId,
        long stake,
        long payout,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("Game id is required.", nameof(gameId));
        if (stake < 0)
            throw new ArgumentOutOfRangeException(nameof(stake), "Stake cannot be negative.");
        if (payout < 0)
            throw new ArgumentOutOfRangeException(nameof(payout), "Payout cannot be negative.");

        Player = player;
        GameId = gameId;
        Stake = stake;
        Payout = payout;
        OccurredAt = occurredAt;
    }

    public GameCommandContext Player { get; }
    public string GameId { get; }
    public long Stake { get; }
    public long Payout { get; }
    public DateTimeOffset OccurredAt { get; }
}

/// <summary>Creates the SDK completion event consumed by meta projections.</summary>
public static class GameCompletionEventFactory
{
    public static GameCompletedMetaEvent Create(GameCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var multiplier = completion.Stake == 0
            ? 0m
            : decimal.Divide(completion.Payout, completion.Stake);
        return new(
            completion.Player.ChatId,
            completion.Player.UserId,
            completion.Player.DisplayName,
            completion.GameId,
            completion.Stake,
            completion.Payout,
            completion.Payout > completion.Stake,
            multiplier,
            completion.OccurredAt.ToUnixTimeMilliseconds());
    }
}
