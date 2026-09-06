using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution.Cards;

/// <summary>
/// Immutable ordered deck. The first card is the top and every card id is
/// unique, allowing multiple cards with an equal game face.
/// </summary>
public sealed class Deck<TCardId, TFace>
    where TCardId : notnull
    where TFace : notnull
{
    public Deck(IEnumerable<Card<TCardId, TFace>> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var copy = cards.ToArray();
        if (copy.Any(static card => card is null))
            throw new ArgumentException("A deck cannot contain null cards.", nameof(cards));
        if (copy.Select(card => card.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("A deck cannot contain the same card id twice.", nameof(cards));

        Cards = new ReadOnlyCollection<Card<TCardId, TFace>>(copy);
    }

    public IReadOnlyList<Card<TCardId, TFace>> Cards { get; }

    public int Count => Cards.Count;

    public bool IsEmpty => Cards.Count == 0;

    public Card<TCardId, TFace> Peek()
    {
        if (Cards.Count == 0)
            throw new InvalidOperationException("Cannot peek an empty deck.");
        return Cards[0];
    }

    /// <summary>Draws from the top and returns both the cards and remaining deck.</summary>
    public DeckDraw<TCardId, TFace> Draw(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > Cards.Count)
            throw new InvalidOperationException("Cannot draw more cards than the deck contains.");

        return new(
            new Deck<TCardId, TFace>(Cards.Skip(count)),
            Cards.Take(count).ToArray());
    }

    /// <summary>
    /// Returns a deterministically shuffled deck. The supplied random source
    /// must be backed by executor-provided entropy, not process randomness.
    /// </summary>
    public Deck<TCardId, TFace> Shuffle(IGameRandom random, string entropyNamePrefix)
    {
        ArgumentNullException.ThrowIfNull(random);
        return new Deck<TCardId, TFace>(random.Shuffle(entropyNamePrefix, Cards));
    }
}
