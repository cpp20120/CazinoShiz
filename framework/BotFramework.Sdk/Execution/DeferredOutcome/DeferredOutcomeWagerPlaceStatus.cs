namespace BotFramework.Sdk.Execution.DeferredOutcome;

public enum DeferredOutcomeWagerPlaceStatus
{
    Accepted,
    InvalidAmount,
    BusyOtherGame,
    AlreadyPending,
    DailyQuotaExceeded,
    InsufficientBalance,
}
