namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>Immutable wager data passed to outcome and event factories.</summary>
public sealed record InstantWagerSettlement(GameCommandContext Player, long Amount, DateTimeOffset OccurredAt);
