namespace BotFramework.Sdk;

/// <summary>
/// Normalized game completion event consumed by meta systems such as seasons,
/// quests, achievements, clans, tournaments, and risk control.
/// Game modules should publish this in addition to their own domain-specific
/// events once their existing payout/refund flow has completed.
/// </summary>
public sealed record GameCompletedMetaEvent(
    long ChatId,
    long UserId,
    string DisplayName,
    string GameKey,
    long Stake,
    long Payout,
    bool IsWin,
    decimal Multiplier,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.game_completed";
}

/// <summary>
/// Normalized transfer event for later quest, clan, season, and risk-control projections.
/// </summary>
public sealed record TransferCompletedMetaEvent(
    long ChatId,
    long FromUserId,
    long ToUserId,
    long DebitTotal,
    long CreditNet,
    long Fee,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.transfer_completed";
}

/// <summary>
/// Normalized daily-claim event for later quest and streak projections.
/// </summary>
public sealed record DailyClaimedMetaEvent(
    long ChatId,
    long UserId,
    string DisplayName,
    long Amount,
    long BalanceAfter,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.daily_claimed";
}

/// <summary>
/// Emitted once per player, game, and local play day after streak progress changes.
/// Mirrored to analytics for retention and per-game streak reporting.
/// </summary>
public sealed record GameStreakUpdatedMetaEvent(
    long SeasonId,
    long ChatId,
    long UserId,
    string GameKey,
    int CurrentStreak,
    int BestStreak,
    int TotalPlayDays,
    DateOnly PlayedOn,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.game_streak_updated";
}

/// <summary>
/// Emitted only when an achievement is newly inserted for the player.
/// </summary>
public sealed record AchievementUnlockedMetaEvent(
    long SeasonId,
    long ChatId,
    long UserId,
    string AchievementId,
    string Title,
    string Category,
    string GameKey,
    long OccurredAt) : IDomainEvent
{
    public string EventType => "meta.achievement_unlocked";
}
