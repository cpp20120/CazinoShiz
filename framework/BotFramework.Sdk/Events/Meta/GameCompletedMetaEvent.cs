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
