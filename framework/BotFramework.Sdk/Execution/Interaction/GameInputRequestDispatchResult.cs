namespace BotFramework.Sdk.Execution;

/// <summary>Result of consuming and routing a frontend interaction.</summary>
public sealed record GameInputRequestDispatchResult(
    GameInputRequestDispatchStatus Status,
    GameInputRequest? Request);
