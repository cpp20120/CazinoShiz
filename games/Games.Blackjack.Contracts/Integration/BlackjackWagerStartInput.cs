namespace Games.Blackjack.Contracts.Integration;

/// <summary>Opaque input captured by Wagering when a blackjack wager is created.</summary>
public sealed record BlackjackWagerStartInput(
    long ChatId,
    string DisplayName,
    int HandTimeoutMs);
