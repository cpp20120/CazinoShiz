using Dapper;

namespace Games.Horse.Infrastructure.Persistence;

public sealed record HorseBetRow(
    Guid Id, string RaceDate, long UserId, long BalanceScopeId, int HorseId, int Amount,
    string? WagerBetId = null);
