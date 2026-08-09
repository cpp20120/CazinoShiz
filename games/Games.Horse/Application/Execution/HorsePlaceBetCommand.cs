namespace Games.Horse.Application.Execution;

public sealed record HorsePlaceBetCommand(
    long UserId,
    string DisplayName,
    long BalanceScopeId,
    int HorseId,
    int Amount,
    string RaceDate,
    Guid BetId,
    string CommandId,
    int HorseCount,
    string? WagerBetId = null);
