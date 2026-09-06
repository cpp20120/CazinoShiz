namespace BotFramework.Sdk.Execution;

/// <summary>Result of attempting to consume an interaction request.</summary>
public enum GameInputRequestConsumeStatus
{
    Accepted,
    AlreadyConsumed,
    NotFound,
    Forbidden,
    Expired,
    Cancelled,
    InvalidValue,
    ConsumedByAnotherCorrelation,
}