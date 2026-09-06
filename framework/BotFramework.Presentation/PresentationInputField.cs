namespace BotFramework.Presentation;

/// <summary>One frontend-rendered field of a generic input form.</summary>
public sealed record PresentationInputField
{
    public PresentationInputField(
        string fieldId,
        PresentationText label,
        PresentationInputKind kind = PresentationInputKind.Text,
        bool required = true,
        PresentationText? hint = null,
        string? initialValue = null)
    {
        FieldId = PresentationValue.Required(fieldId, nameof(fieldId));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Kind = kind;
        Required = required;
        Hint = hint;
        InitialValue = initialValue;
    }

    public string FieldId { get; }

    public PresentationText Label { get; }

    public PresentationInputKind Kind { get; }

    public bool Required { get; }

    public PresentationText? Hint { get; }

    /// <summary>Optional presentation default; the game must validate submitted input itself.</summary>
    public string? InitialValue { get; }
}
