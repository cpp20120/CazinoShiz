namespace BotFramework.Narrative;

/// <summary>Address of one durable narrative projection.</summary>
public sealed record NarrativeProjectionKey(NarrativeAddress Target, NarrativeProjectionScope? Scope = null)
{
    public NarrativeAddress Target { get; } = Target ?? throw new ArgumentNullException(nameof(Target));
}
