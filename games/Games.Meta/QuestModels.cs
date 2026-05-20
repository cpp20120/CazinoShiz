namespace Games.Meta;

public sealed record QuestTemplate(
    string Id,
    string Title,
    string Description,
    string Period,
    string Kind,
    string? GameKey,
    int Target,
    long RewardXp,
    long RewardCoins);

public sealed record PlayerQuestView(
    string Id,
    string Title,
    string Description,
    string Period,
    int Progress,
    int Target,
    bool Completed,
    bool Claimed,
    long RewardXp,
    long RewardCoins);

public sealed record QuestProgressUpdate(
    string QuestId,
    int Progress,
    int Target,
    bool Completed);

public sealed record QuestClaimResult(
    string QuestId,
    string Title,
    long RewardXp,
    long RewardCoins,
    bool Claimed);
