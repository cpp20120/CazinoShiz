namespace BotFramework.Contracts.Wagering;

/// <summary>Durable lifecycle of a wager operation exposed to asynchronous clients.</summary>
public enum WagerOperationStatus
{
    Pending = 0,
    Reserving = 1,
    Playing = 2,
    Settling = 3,
    Completed = 4,
    Rejected = 5,
    Failed = 6,
    Compensating = 7,
    Compensated = 8,
}
