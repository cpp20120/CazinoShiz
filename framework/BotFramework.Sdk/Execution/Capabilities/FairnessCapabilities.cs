namespace BotFramework.Sdk.Execution;

/// <summary>Capabilities for deterministic and verifiable random outcomes.</summary>
public static class FairnessCapabilities
{
    public static GameCapability Randomness { get; } = new("fairness.randomness");

    public static GameCapability VerifiableOutcomes { get; } = new("fairness.verifiable-outcomes");
}
