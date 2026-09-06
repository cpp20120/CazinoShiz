namespace BotFramework.Sdk.Execution;

/// <summary>Lifecycle of one player-scoped, one-time interaction request.</summary>
public enum GameInputRequestStatus
{
    Pending,
    Consumed,
    Expired,
    Cancelled,
}
