using BotFramework.Sdk.Execution;

namespace BotFramework.Narrative;

/// <summary>Sets or clears a named flag in the recipient's narrative projection.</summary>
public sealed record NarrativeFlagEffect : NarrativeEffect
{
    public NarrativeFlagEffect(NarrativeAddress target, string flag, bool value = true)
        : base(target)
    {
        Flag = NarrativeValue.Required(flag, nameof(flag));
        Value = value;
    }

    public string Flag { get; }

    public bool Value { get; }

    public override GameCapability RequiredCapability => NarrativeCapabilities.Flags;
}
