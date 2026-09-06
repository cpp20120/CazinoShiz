namespace BotFramework.Sdk.Execution;

/// <summary>
/// Marks an external effect that must be persisted with the game transaction
/// and delivered only after that transaction commits.
/// </summary>
public interface IDurableGameEffect : IGameEffect;
