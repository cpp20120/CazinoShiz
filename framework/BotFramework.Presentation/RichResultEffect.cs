using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>Delivers a structured result such as a score, payout or round summary.</summary>
public sealed record RichResultEffect : PresentationEffect
{
    public RichResultEffect(
        PresentationAddress target,
        string resultId,
        PresentationText title,
        PresentationText? body = null,
        IReadOnlyList<PresentationField>? fields = null,
        PresentationTone tone = PresentationTone.Neutral)
        : base(target)
    {
        ResultId = PresentationValue.Required(resultId, nameof(resultId));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Body = body;
        Fields = PresentationValue.CopyOptional(fields, nameof(fields));
        if (Fields.Select(field => field.FieldId).Distinct(StringComparer.Ordinal).Count() != Fields.Count)
            throw new ArgumentException("Field ids must be unique.", nameof(fields));

        Tone = tone;
    }

    /// <summary>Game-owned id that lets a frontend update or deduplicate a result card.</summary>
    public string ResultId { get; }

    public PresentationText Title { get; }

    public PresentationText? Body { get; }

    public IReadOnlyList<PresentationField> Fields { get; }

    public PresentationTone Tone { get; }

    public override GameCapability RequiredCapability => MessagingCapabilities.RichResults;
}
