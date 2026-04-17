namespace CasinoShiz.Services.Leaderboard;

public sealed record LeaderboardUser(
    long TelegramUserId,
    string DisplayName,
    int Coins,
    long LastDayUtc,
    int AttemptCount,
    int ExtraAttempts);

public sealed record LeaderboardPlace(int Place, List<LeaderboardUser> Users);

public sealed record Leaderboard(List<LeaderboardPlace> Places, bool Truncated);

public sealed record BalanceInfo(int Coins, bool Visible);
