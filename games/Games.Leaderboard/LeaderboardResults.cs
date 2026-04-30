namespace Games.Leaderboard;

public sealed record LeaderboardUser(
    long TelegramUserId, long BalanceScopeId, string DisplayName, int Coins, long UpdatedAtUnixMs);

public sealed record LeaderboardPlace(int Place, List<LeaderboardUser> Users);

public sealed record Leaderboard(List<LeaderboardPlace> Places, bool Truncated);

public sealed record BalanceInfo(int Coins, bool Visible);

public sealed record GlobalLeaderboardUser(
    long TelegramUserId, string DisplayName, int TotalCoins, int ChatCount);

public sealed record GlobalLeaderboardPlace(int Place, List<GlobalLeaderboardUser> Users);

public sealed record GlobalLeaderboard(List<GlobalLeaderboardPlace> Places, bool Truncated, int TotalUsers);

public sealed record ChatLeaderboard(
    long ChatId,
    string? Title,
    string ChatType,
    List<LeaderboardPlace> Places,
    bool Truncated);

public sealed record MultiChatLeaderboard(List<ChatLeaderboard> Chats);
