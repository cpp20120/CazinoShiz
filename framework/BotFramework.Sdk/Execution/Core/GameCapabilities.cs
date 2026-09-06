namespace BotFramework.Sdk.Execution;

/// <summary>
/// Declared execution styles exposed by a game to transport adapters and
/// catalogs. Runtime feature requirements are declared separately through
/// <see cref="GameDefinition.RequiredCapabilities"/>.
/// </summary>
[Flags]
public enum GameCapabilities
{
    None = 0,
    InstantWager = 1,
    DeferredOutcomeWager = 2,
    TurnBased = 4,
    RoundBased = 8,
    Multiplayer = 16,
    FairRandomness = 32,
}
