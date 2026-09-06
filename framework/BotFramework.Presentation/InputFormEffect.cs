using BotFramework.Sdk.Execution;

namespace BotFramework.Presentation;

/// <summary>
/// Renders a form for a previously emitted <see cref="InputRequestEffect"/>.
/// The adapter submits one JSON object through that request; the route handler
/// remains responsible for validating every field as untrusted input.
/// </summary>
public sealed record InputFormEffect : PresentationEffect
{
    public InputFormEffect(
        PresentationAddress target,
        string requestId,
        PresentationText title,
        IReadOnlyList<PresentationInputField> fields,
        PresentationText? description = null,
        PresentationText? submitLabel = null)
        : base(target)
    {
        RequestId = PresentationValue.Required(requestId, nameof(requestId));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Fields = PresentationValue.CopyRequired(fields, nameof(fields));
        if (Fields.Select(field => field.FieldId).Distinct(StringComparer.Ordinal).Count() != Fields.Count)
            throw new ArgumentException("Field ids must be unique.", nameof(fields));

        Description = description;
        SubmitLabel = submitLabel;
    }

    /// <summary>Must equal the companion <see cref="InputRequestEffect.RequestId"/>.</summary>
    public string RequestId { get; }

    public PresentationText Title { get; }

    public IReadOnlyList<PresentationInputField> Fields { get; }

    public PresentationText? Description { get; }

    public PresentationText? SubmitLabel { get; }

    public override GameCapability RequiredCapability => MessagingCapabilities.Input;
}
