namespace BotFramework.Sdk.Execution;

/// <summary>Outcome of the atomic player/scope/value validation and consumption step.</summary>
public sealed record GameInputRequestConsumeResult(
    GameInputRequestConsumeStatus Status,
    GameInputRequest? Request);
