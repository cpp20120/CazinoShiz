using System.Collections.ObjectModel;

namespace BotFramework.Sdk.Execution.Cards;

/// <summary>
/// Immutable ordered hand. Ownership and visibility are game concerns; combine
/// it with a player id and <see cref="GameAudience"/> in the game state.
/// </summary>
public sealed class Hand<TCardId, TFace>
    where TCardId : notnull
    where TFace : notnull
{
    public Hand(IEnumerable<Card<TCardId, TFace>> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var copy = cards.ToArray();
        if (copy.Any(static card => card is null))
            throw new ArgumentException("A hand cannot contain null cards.", nameof(cards));
        if (copy.Select(card => card.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("A hand cannot contain the same card id twice.", nameof(cards));

        Cards = new ReadOnlyCollection<Card<TCardId, TFace>>(copy);
    }

    public IReadOnlyList<Card<TCardId, TFace>> Cards { get; }

    public int Count => Cards.Count;

    public bool Contains(TCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        return Cards.Any(card => EqualityComparer<TCardId>.Default.Equals(card.Id, cardId));
    }

    public Hand<TCardId, TFace> Add(IEnumerable<Card<TCardId, TFace>> cards) =>
        new(Cards.Concat(cards ?? throw new ArgumentNullException(nameof(cards))));

    public Hand<TCardId, TFace> Remove(TCardId cardId)
    {
        ArgumentNullException.ThrowIfNull(cardId);
        if (!Contains(cardId))
            throw new KeyNotFoundException($"Card '{cardId}' is not in this hand.");

        return new Hand<TCardId, TFace>(
            Cards.Where(card => !EqualityComparer<TCardId>.Default.Equals(card.Id, cardId)));
    }
}
