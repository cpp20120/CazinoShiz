using Games.Poker;
using Games.SecretHitler;
using Games.SecretHitler.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerResultHelpersTests
{
    [Theory]
    [InlineData(PokerError.NotEnoughCoins)]
    [InlineData(PokerError.TableNotFound)]
    [InlineData(PokerError.TableFull)]
    public void Fail_ReturnsCreateResultWithError(PokerError error)
    {
        var r = PokerResultHelpers.Fail(error);
        Assert.Equal(error, r.Error);
        Assert.Equal("", r.InviteCode);
        Assert.Equal(0, r.BuyIn);
    }

    [Theory]
    [InlineData(PokerError.TableNotFound)]
    [InlineData(PokerError.AlreadySeated)]
    public void JoinFail_ReturnsJoinResultWithError(PokerError error)
    {
        var r = PokerResultHelpers.JoinFail(error);
        Assert.Equal(error, r.Error);
        Assert.Null(r.Snapshot);
        Assert.Equal(0, r.Seated);
        Assert.Equal(0, r.Max);
    }

    [Theory]
    [InlineData(PokerError.TableNotFound)]
    [InlineData(PokerError.NoTable)]
    public void LeaveFail_ReturnsLeaveResultWithError(PokerError error)
    {
        var r = PokerResultHelpers.LeaveFail(error);
        Assert.Equal(error, r.Error);
        Assert.Null(r.Snapshot);
        Assert.False(r.TableClosed);
    }

    [Theory]
    [InlineData(PokerError.NotHost)]
    [InlineData(PokerError.NeedTwo)]
    public void StartFail_ReturnsStartResultWithError(PokerError error)
    {
        var r = PokerResultHelpers.StartFail(error);
        Assert.Equal(error, r.Error);
        Assert.Null(r.Snapshot);
    }

    [Theory]
    [InlineData(PokerError.NotYourTurn)]
    [InlineData(PokerError.CannotCheck)]
    [InlineData(PokerError.RaiseTooSmall)]
    [InlineData(PokerError.RaiseTooLarge)]
    [InlineData(PokerError.InvalidAction)]
    public void ActionFail_ReturnsActionResultWithError(PokerError error)
    {
        var r = PokerResultHelpers.ActionFail(error);
        Assert.Equal(error, r.Error);
        Assert.Null(r.Snapshot);
        Assert.Equal(HandTransition.None, r.Transition);
        Assert.Null(r.Showdown);
        Assert.Null(r.AutoActorName);
        Assert.Null(r.AutoKind);
    }
}

public class ShResultHelpersTests
{
    [Theory]
    [InlineData(ShError.NotEnoughCoins)]
    [InlineData(ShError.GameInProgress)]
    public void CreateFail_ReturnsResultWithError(ShError error)
    {
        var r = ShResultHelpers.CreateFail(error);
        Assert.Equal(error, r.Error);
        Assert.Equal("", r.InviteCode);
        Assert.Equal(0, r.BuyIn);
    }

    [Theory]
    [InlineData(ShError.GameFull)]
    [InlineData(ShError.AlreadyInGame)]
    public void JoinFail_ReturnsResultWithError(ShError error)
    {
        var r = ShResultHelpers.JoinFail(error);
        Assert.Equal(error, r.Error);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void LeaveFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.LeaveFail(ShError.NotInGame);
        Assert.Equal(ShError.NotInGame, r.Error);
        Assert.Null(r.Snapshot);
        Assert.False(r.GameClosed);
    }

    [Fact]
    public void StartFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.StartFail(ShError.NotEnoughPlayers);
        Assert.Equal(ShError.NotEnoughPlayers, r.Error);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void NominateFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.NominateFail(ShError.WrongPhase);
        Assert.Equal(ShError.WrongPhase, r.Error);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void VoteFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.VoteFail(ShError.AlreadyVoted);
        Assert.Equal(ShError.AlreadyVoted, r.Error);
        Assert.Null(r.Snapshot);
        Assert.Null(r.After);
    }

    [Fact]
    public void DiscardFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.DiscardFail(ShError.NotPresident);
        Assert.Equal(ShError.NotPresident, r.Error);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void EnactFail_ReturnsResultWithError()
    {
        var r = ShResultHelpers.EnactFail(ShError.NotChancellor);
        Assert.Equal(ShError.NotChancellor, r.Error);
        Assert.Null(r.Snapshot);
        Assert.Null(r.After);
    }

    // ── MapValidation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ShValidation.NotPresident,   ShError.NotPresident)]
    [InlineData(ShValidation.NotChancellor,  ShError.NotChancellor)]
    [InlineData(ShValidation.NotYourTurn,    ShError.WrongPhase)]
    [InlineData(ShValidation.InvalidTarget,  ShError.InvalidTarget)]
    [InlineData(ShValidation.TermLimited,    ShError.TermLimited)]
    [InlineData(ShValidation.AlreadyVoted,   ShError.AlreadyVoted)]
    [InlineData(ShValidation.WrongPhase,     ShError.WrongPhase)]
    [InlineData(ShValidation.InvalidPolicy,  ShError.InvalidPolicy)]
    [InlineData(ShValidation.Ok,             ShError.None)]
    public void MapValidation_MapsAllValues(ShValidation validation, ShError expected)
    {
        Assert.Equal(expected, ShResultHelpers.MapValidation(validation));
    }
}
