namespace Games.Leaderboard;

public sealed record LeaderboardUser(
    long TelegramUserId, long BalanceScopeId, string DisplayName, int Coins, long UpdatedAtUnixMs);

public sealed record LeaderboardPlace(int Place, List<LeaderboardUser> Users);

public sealed record Leaderboard(List<LeaderboardPlace> Places, bool Truncated);

public sealed record BalanceInfo(int Coins, bool Visible);
