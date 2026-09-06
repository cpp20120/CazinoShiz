namespace BotFramework.Sdk.Execution;

/// <summary>
/// Stable, transport-neutral name of a runtime feature a game can require or
/// an adapter can provide.
/// </summary>
public sealed record GameCapability
{
    public GameCapability(string id)
    {
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Capability id is required.", nameof(id))
            : id;
    }

    public string Id { get; }

    public override string ToString() => Id;
}
