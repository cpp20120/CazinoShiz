namespace BotFramework.Sdk;
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
