namespace BotFramework.Sdk.Execution;

/// <summary>One named entropy value consumed by a deterministic game rule.</summary>
public sealed record GameRandomDraw(string Name, double UnitValue);
