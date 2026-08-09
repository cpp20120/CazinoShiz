namespace Games.SecretHitler.Application.Wagering;

/// <summary>Secret Hitler wager state without actor wallet snapshots.</summary>
public sealed record SecretHitlerWagerState(
    SecretHitlerGame? Game,
    List<SecretHitlerPlayer> Players);
