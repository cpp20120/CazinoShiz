using Games.Poker.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerActionTests
{
    // ── FromVerb ─────────────────────────────────────────────────────────────

    [Fact]
    public void FromVerb_Check_ReturnsCheckAction()
    {
        var action = PokerAction.FromVerb("check", 0);
        Assert.NotNull(action);
        Assert.Equal(PokerActionKind.Check, action!.Value.Kind);
    }

    [Fact]
    public void FromVerb_Call_ReturnsCallAction()
    {
        var action = PokerAction.FromVerb("call", 0);
        Assert.NotNull(action);
        Assert.Equal(PokerActionKind.Call, action!.Value.Kind);
    }

    [Fact]
    public void FromVerb_Fold_ReturnsFoldAction()
    {
        var action = PokerAction.FromVerb("fold", 0);
        Assert.NotNull(action);
        Assert.Equal(PokerActionKind.Fold, action!.Value.Kind);
    }

    [Fact]
    public void FromVerb_AllIn_ReturnsAllInAction()
    {
        var action = PokerAction.FromVerb("allin", 0);
        Assert.NotNull(action);
        Assert.Equal(PokerActionKind.AllIn, action!.Value.Kind);
    }

    [Fact]
    public void FromVerb_Raise_ReturnsRaiseAction()
    {
        var action = PokerAction.FromVerb("raise", 200);
        Assert.NotNull(action);
        Assert.Equal(PokerActionKind.Raise, action!.Value.Kind);
    }

    [Fact]
    public void FromVerb_Raise_PreservesAmount()
    {
        var action = PokerAction.FromVerb("raise", 350);
        Assert.Equal(350, action!.Value.Amount);
    }

    [Fact]
    public void FromVerb_Check_AmountIsZero()
    {
        var action = PokerAction.FromVerb("check", 999);
        Assert.Equal(0, action!.Value.Amount);
    }

    [Theory]
    [InlineData("CHECK")]
    [InlineData("FOLD")]
    [InlineData("Raise")]
    [InlineData("Call")]
    [InlineData("bet")]
    [InlineData("")]
    [InlineData("unknown")]
    public void FromVerb_InvalidOrUpperCase_ReturnsNull(string verb)
    {
        var action = PokerAction.FromVerb(verb, 0);
        Assert.Null(action);
    }

    // ── Static factory methods ────────────────────────────────────────────────

    [Fact]
    public void Check_StaticFactory_ReturnsCheckKind()
    {
        var action = PokerAction.Check();
        Assert.Equal(PokerActionKind.Check, action.Kind);
        Assert.Equal(0, action.Amount);
    }

    [Fact]
    public void Fold_StaticFactory_ReturnsFoldKind()
    {
        var action = PokerAction.Fold();
        Assert.Equal(PokerActionKind.Fold, action.Kind);
    }
}
