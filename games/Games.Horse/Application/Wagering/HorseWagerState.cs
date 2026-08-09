namespace Games.Horse.Application.Wagering;

/// <summary>Horse wager game state. The reserved stake remains in Wagering terms.</summary>
public sealed record HorseWagerState(
    Guid? BetId,
    string? RaceDate,
    long PlayerId,
    long BalanceScopeId,
    int HorseId);
