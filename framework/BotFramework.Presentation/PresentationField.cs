namespace BotFramework.Presentation;

/// <summary>One labeled, localizable value rendered in a rich game result.</summary>
public sealed record PresentationField
{
    public PresentationField(string fieldId, PresentationText label, PresentationText value)
    {
        FieldId = PresentationValue.Required(fieldId, nameof(fieldId));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string FieldId { get; }

    public PresentationText Label { get; }

    public PresentationText Value { get; }
}
