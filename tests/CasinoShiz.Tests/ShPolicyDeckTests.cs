using Games.SecretHitler.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class ShPolicyDeckTests
{
    [Fact]
    public void BuildShuffledDeck_containsSixLiberalAndElevenFascist()
    {
        var deck = ShPolicyDeck.BuildShuffledDeck();

        Assert.Equal(17, deck.Length);
        Assert.Equal(6, deck.Count(c => c == 'L'));
        Assert.Equal(11, deck.Count(c => c == 'F'));
    }

    [Fact]
    public void SerializeParse_roundtripsPreservingOrder()
    {
        var policies = new[] { ShPolicy.Liberal, ShPolicy.Fascist, ShPolicy.Fascist, ShPolicy.Liberal };

        var serialized = ShPolicyDeck.Serialize(policies);
        var parsed = ShPolicyDeck.Parse(serialized);

        Assert.Equal("LFFL", serialized);
        Assert.Equal(policies, parsed);
    }

    [Fact]
    public void Draw_takesFromTopOfDeck()
    {
        string deck = "LFFLL";
        string discard = "";

        var drawn = ShPolicyDeck.Draw(ref deck, ref discard, 3);

        Assert.Equal(new[] { ShPolicy.Liberal, ShPolicy.Fascist, ShPolicy.Fascist }, drawn);
        Assert.Equal("LL", deck);
        Assert.Equal("", discard);
    }

    [Fact]
    public void Draw_whenDeckTooSmall_reshufflesDiscardIntoDeck()
    {
        string deck = "L";
        string discard = "FFLF";

        var drawn = ShPolicyDeck.Draw(ref deck, ref discard, 3);

        Assert.Equal(3, drawn.Length);
        Assert.Equal("", discard);
        Assert.Equal(2, deck.Length);
        var leftover = ShPolicyDeck.Parse(deck);
        var totalL = drawn.Count(p => p == ShPolicy.Liberal) + leftover.Count(p => p == ShPolicy.Liberal);
        var totalF = drawn.Count(p => p == ShPolicy.Fascist) + leftover.Count(p => p == ShPolicy.Fascist);
        Assert.Equal(2, totalL);
        Assert.Equal(3, totalF);
    }

    [Fact]
    public void AddToDiscard_appendsPolicyCharacter()
    {
        string discard = "LF";

        ShPolicyDeck.AddToDiscard(ref discard, ShPolicy.Liberal);
        ShPolicyDeck.AddToDiscard(ref discard, ShPolicy.Fascist);

        Assert.Equal("LFLF", discard);
    }

    [Fact]
    public void PeekTop_returnsFirstCardWithoutMutating()
    {
        var top = ShPolicyDeck.PeekTop("FLLF", "LL");
        Assert.Equal(ShPolicy.Fascist, top);
    }

    [Fact]
    public void PeekTop_whenDeckEmpty_usesDiscard()
    {
        var top = ShPolicyDeck.PeekTop("", "LFF");
        Assert.Equal(ShPolicy.Liberal, top);
    }
}
