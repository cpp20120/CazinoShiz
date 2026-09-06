using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution.Cards;

/// <summary>Immutable result of drawing cards from a deck.</summary>
public sealed record DeckDraw<TCardId, TFace>
    where TCardId : notnull
    where TFace : notnull
{
    public DeckDraw(Deck<TCardId, TFace> remainingDeck, IEnumerable<Card<TCardId, TFace>> cards)
    {
        ArgumentNullException.ThrowIfNull(remainingDeck);
        ArgumentNullException.ThrowIfNull(cards);
        var copy = cards.ToArray();
        if (copy.Any(static card => card is null))
            throw new ArgumentException("A draw cannot contain null cards.", nameof(cards));

        RemainingDeck = remainingDeck;
        Cards = new ReadOnlyCollection<Card<TCardId, TFace>>(copy);
    }

    public Deck<TCardId, TFace> RemainingDeck { get; }

    public IReadOnlyList<Card<TCardId, TFace>> Cards { get; }
}
