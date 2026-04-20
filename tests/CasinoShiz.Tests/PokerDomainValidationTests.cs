using Games.Poker;
using Games.Poker.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerDomainValidationTests
{
    private static PokerTable MakeTable(int currentBet = 0, int bigBlind = 10, int minRaise = 10) => new()
    {
        CurrentBet = currentBet,
        BigBlind = bigBlind,
        MinRaise = minRaise,
        Status = PokerTableStatus.HandActive,
        Phase = PokerPhase.PreFlop,
    };

    private static PokerSeat MakeSeat(int stack = 500, int currentBet = 0) => new()
    {
        Stack = stack,
        CurrentBet = currentBet,
        Status = PokerSeatStatus.Seated,
    };

    [Fact]
    public void Validate_Check_NoBetToCall_ReturnsOk()
    {
        var table = MakeTable(currentBet: 0);
        var seat = MakeSeat(currentBet: 0);
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, PokerAction.Check()));
    }

    [Fact]
    public void Validate_Check_BetToCall_ReturnsCannotCheck()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(currentBet: 0);
        Assert.Equal(ValidationResult.CannotCheck, PokerDomain.Validate(table, seat, PokerAction.Check()));
    }

    [Fact]
    public void Validate_Check_SeatMatchesCurrentBet_ReturnsOk()
    {
        var table = MakeTable(currentBet: 10);
        var seat = MakeSeat(currentBet: 10);
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, PokerAction.Check()));
    }

    [Fact]
    public void Validate_Fold_ReturnsOk()
    {
        var table = MakeTable(currentBet: 50);
        var seat = MakeSeat();
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, PokerAction.Fold()));
    }

    [Fact]
    public void Validate_AllIn_WithStack_ReturnsOk()
    {
        var table = MakeTable();
        var seat = MakeSeat(stack: 100);
        var action = new PokerAction(PokerActionKind.AllIn);
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, action));
    }

    [Fact]
    public void Validate_AllIn_EmptyStack_ReturnsInvalid()
    {
        var table = MakeTable();
        var seat = MakeSeat(stack: 0);
        var action = new PokerAction(PokerActionKind.AllIn);
        Assert.Equal(ValidationResult.Invalid, PokerDomain.Validate(table, seat, action));
    }

    [Fact]
    public void Validate_Raise_BelowMinimum_ReturnsRaiseTooSmall()
    {
        var table = MakeTable(currentBet: 10, bigBlind: 10, minRaise: 10);
        var seat = MakeSeat(stack: 500, currentBet: 0);
        // minTotal = 10 + max(10, 10) = 20; raise to 15 is too small
        var action = new PokerAction(PokerActionKind.Raise, 15);
        Assert.Equal(ValidationResult.RaiseTooSmall, PokerDomain.Validate(table, seat, action));
    }

    [Fact]
    public void Validate_Raise_AboveMaximum_ReturnsRaiseTooLarge()
    {
        var table = MakeTable(currentBet: 10, bigBlind: 10, minRaise: 10);
        var seat = MakeSeat(stack: 100, currentBet: 0);
        // maxTotal = 0 + 100 = 100; raise to 200 is too large
        var action = new PokerAction(PokerActionKind.Raise, 200);
        Assert.Equal(ValidationResult.RaiseTooLarge, PokerDomain.Validate(table, seat, action));
    }

    [Fact]
    public void Validate_Raise_ValidAmount_ReturnsOk()
    {
        var table = MakeTable(currentBet: 10, bigBlind: 10, minRaise: 10);
        var seat = MakeSeat(stack: 500, currentBet: 0);
        // minTotal = 20, maxTotal = 500; raise to 30 is valid
        var action = new PokerAction(PokerActionKind.Raise, 30);
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, action));
    }

    [Fact]
    public void Validate_Call_ReturnsOk()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(stack: 200, currentBet: 0);
        var action = new PokerAction(PokerActionKind.Call);
        Assert.Equal(ValidationResult.Ok, PokerDomain.Validate(table, seat, action));
    }
}
