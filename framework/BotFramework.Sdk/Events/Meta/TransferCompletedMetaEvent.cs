namespace BotFramework.Sdk;
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
