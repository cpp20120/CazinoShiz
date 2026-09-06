namespace BotFramework.Sdk.Execution.Cards;

/// <summary>
/// One physical card. <see cref="Id"/> distinguishes copies of the same face,
/// while <see cref="Face"/> contains game-defined rank, suit, effect or art.
/// </summary>
public sealed record Card<TCardId, TFace>
    where TCardId : notnull
    where TFace : notnull
{
    public Card(TCardId id, TFace face)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(face);
        Id = id;
        Face = face;
    }

    public TCardId Id { get; }

    public TFace Face { get; }
}
