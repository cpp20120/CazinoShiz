namespace BotFramework.Sdk.Execution.Lifecycle;

/// <summary>Result of an atomic durable session write.</summary>
public sealed record GameSessionWriteResult(GameSessionWriteStatus Status, GameSession Session);
