namespace Games.Meta;

public sealed record TournamentInfo(
    long Id,
    long SeasonId,
    long ChatId,
    string GameKey,
    string Type,
    string Status,
    int EntryFee,
    int MaxPlayers,
    long CreatedBy,
    DateTimeOffset CreatedAt,
    int PlayerCount,
    long PrizePool);

public sealed record TournamentPlayerInfo(
    long TournamentId,
    long UserId,
    string DisplayName,
    string Status,
    DateTimeOffset JoinedAt);

public sealed record TournamentMatchInfo(
    long Id,
    long TournamentId,
    int Round,
    int MatchIndex,
    string Status,
    long? Player1UserId,
    string? Player1DisplayName,
    long? Player2UserId,
    string? Player2DisplayName,
    long? VictorUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TournamentCreateResult(bool Created, string Message, TournamentInfo? Tournament = null);
public sealed record TournamentJoinResult(bool Joined, string Message, TournamentInfo? Tournament = null);
public sealed record TournamentReportResult(bool Updated, bool Finished, string Message, TournamentMatchInfo? Match = null, TournamentPlayerInfo? Victor = null);
