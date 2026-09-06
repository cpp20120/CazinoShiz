namespace BotFramework.Sdk.Execution;

public enum GameInputRequestDispatchStatus
{
    Dispatched,
    NotFound,
    Forbidden,
    Expired,
    Cancelled,
    InvalidValue,
    ConsumedByAnotherCorrelation,
    NoRoute,
}
