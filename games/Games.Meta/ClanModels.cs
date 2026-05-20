namespace Games.Meta;

public sealed record ClanInfo(
    long Id,
    long ChatId,
    string Name,
    string Tag,
    long OwnerUserId,
    DateTimeOffset CreatedAt,
    int MemberCount,
    long SeasonXp,
    int SeasonRating);

public sealed record ClanMemberInfo(
    long ClanId,
    long UserId,
    string DisplayName,
    string Role,
    DateTimeOffset JoinedAt);

public sealed record ClanLeaderboardEntry(
    int Place,
    long ClanId,
    string Name,
    string Tag,
    int Members,
    long Xp,
    int Rating);

public sealed record ClanCreateResult(bool Created, string Message, ClanInfo? Clan = null);
public sealed record ClanJoinResult(bool Joined, string Message, ClanInfo? Clan = null);
