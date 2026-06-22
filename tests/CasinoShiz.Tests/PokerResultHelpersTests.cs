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
