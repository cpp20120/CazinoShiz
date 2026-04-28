using Games.Poker;
using Games.Poker.Domain;
using Xunit;

namespace CasinoShiz.Tests;

public class PokerDomainApplyTests
{
    // ── Factories ────────────────────────────────────────────────────────────

    private static PokerTable MakeTable(
        int bb = 10, int sb = 5, int pot = 0, int currentBet = 0, int minRaise = 10,
        PokerPhase phase = PokerPhase.PreFlop,
        PokerTableStatus status = PokerTableStatus.Seating,
        string deck = "2S 3S 4S 5S 6S 7S 8S 9S TS JS QS KS AS 2H 3H 4H 5H 6H 7H 8H") => new()
    {
        SmallBlind = sb,
        BigBlind = bb,
        Pot = pot,
        CurrentBet = currentBet,
        MinRaise = minRaise,
        Phase = phase,
        Status = status,
        DeckState = deck,
        CommunityCards = "",
    };

    private static PokerSeat MakeSeat(int pos, int stack = 500, int currentBet = 0,
        PokerSeatStatus status = PokerSeatStatus.Seated,
        bool hasActed = false,
        string holeCards = "",
        int totalCommitted = 0) => new()
    {
        Position = pos,
        Stack = stack,
        CurrentBet = currentBet,
        TotalCommitted = totalCommitted,
        Status = status,
        HasActedThisRound = hasActed,
        HoleCards = holeCards,
        UserId = pos, // use position as stable userId in tests
    };

    // ── StartHand ────────────────────────────────────────────────────────────

    [Fact]
    public void StartHand_SetsPhaseToPreFlop()
    {
        var table = MakeTable();
        var seats = new List<PokerSeat> { MakeSeat(1), MakeSeat(2) };
        PokerDomain.StartHand(table, seats);
        Assert.Equal(PokerPhase.PreFlop, table.Phase);
    }

    [Fact]
    public void StartHand_SetsStatusToHandActive()
    {
        var table = MakeTable();
        var seats = new List<PokerSeat> { MakeSeat(1), MakeSeat(2) };
        PokerDomain.StartHand(table, seats);
        Assert.Equal(PokerTableStatus.HandActive, table.Status);
    }

    [Fact]
    public void StartHand_DealsHoleCardsToEachPlayer()
    {
        var table = MakeTable();
        var seats = new List<PokerSeat> { MakeSeat(1), MakeSeat(2), MakeSeat(3) };
        PokerDomain.StartHand(table, seats);
        foreach (var s in seats)
        {
            Assert.NotEmpty(s.HoleCards);
            Assert.Equal(2, s.HoleCards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        }
    }

    [Fact]
    public void StartHand_PostsBlinds_PotEqualsSmallPlusBlind()
    {
        var table = MakeTable(sb: 5, bb: 10);
        var seats = new List<PokerSeat> { MakeSeat(1, stack: 500), MakeSeat(2, stack: 500) };
        PokerDomain.StartHand(table, seats);
        Assert.Equal(15, table.Pot);
    }

    [Fact]
    public void StartHand_CurrentBetEqualsBigBlind()
    {
        var table = MakeTable(bb: 10);
        var seats = new List<PokerSeat> { MakeSeat(1), MakeSeat(2) };
        PokerDomain.StartHand(table, seats);
        Assert.Equal(10, table.CurrentBet);
    }

    [Fact]
    public void StartHand_HeadsUp_ButtonPostsSmallBlind()
    {
        var table = MakeTable(sb: 5, bb: 10);
        var seat1 = MakeSeat(1, stack: 500);
        var seat2 = MakeSeat(2, stack: 500);
        var seats = new List<PokerSeat> { seat1, seat2 };
        PokerDomain.StartHand(table, seats);
        // In heads-up, button = SB. Button is seat at position 1 (first playable).
        var btnSeat = seats.First(s => s.Position == table.ButtonSeat);
        Assert.Equal(5, btnSeat.CurrentBet);
    }

    [Fact]
    public void StartHand_ThreePlayers_SmallBlindIsNextAfterButton()
    {
        var table = MakeTable(sb: 5, bb: 10);
        var seats = new List<PokerSeat> { MakeSeat(1, 500), MakeSeat(2, 500), MakeSeat(3, 500) };
        PokerDomain.StartHand(table, seats);

        // SB is the player next after button with 5 chips bet
        var btn = table.ButtonSeat;
        var ordered = seats.OrderBy(s => s.Position).ToList();
        var btnIdx = ordered.FindIndex(s => s.Position == btn);
        var sbPos = ordered[(btnIdx + 1) % ordered.Count].Position;
        var sbSeat = seats.First(s => s.Position == sbPos);
        Assert.Equal(5, sbSeat.CurrentBet);
    }

    [Fact]
    public void StartHand_SkipsSittingOutPlayers()
    {
        var table = MakeTable();
        var active1 = MakeSeat(1, stack: 500);
        var broke = MakeSeat(2, stack: 0);
        var active2 = MakeSeat(3, stack: 500);
        var seats = new List<PokerSeat> { active1, broke, active2 };
        PokerDomain.StartHand(table, seats);

        Assert.Empty(broke.HoleCards);
        Assert.Equal(PokerSeatStatus.SittingOut, broke.Status);
        Assert.NotEmpty(active1.HoleCards);
        Assert.NotEmpty(active2.HoleCards);
    }

    [Fact]
    public void StartHand_ResetsHoleCardsFromPreviousHand()
    {
        var table = MakeTable();
        var seat = MakeSeat(1, holeCards: "AS KS");
        var seats = new List<PokerSeat> { seat, MakeSeat(2) };
        PokerDomain.StartHand(table, seats);
        // After StartHand, HoleCards is reassigned — old value gone
        Assert.NotEqual("AS KS", seat.HoleCards);
    }

    // ── Apply ────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_Check_DoesNotMutateStackOrPot()
    {
        var table = MakeTable(currentBet: 0, pot: 30);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, PokerAction.Check());
        Assert.Equal(200, seat.Stack);
        Assert.Equal(30, table.Pot);
    }

    [Fact]
    public void Apply_Fold_SetsStatusFolded()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, PokerAction.Fold());
        Assert.Equal(PokerSeatStatus.Folded, seat.Status);
    }

    [Fact]
    public void Apply_Call_ReducesStackByToCall()
    {
        var table = MakeTable(currentBet: 20, pot: 20);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Call));
        Assert.Equal(180, seat.Stack);
    }

    [Fact]
    public void Apply_Call_IncreasesCurrentBetToTableBet()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Call));
        Assert.Equal(20, seat.CurrentBet);
    }

    [Fact]
    public void Apply_Call_AddsToPot()
    {
        var table = MakeTable(currentBet: 20, pot: 20);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Call));
        Assert.Equal(40, table.Pot);
    }

    [Fact]
    public void Apply_Call_TracksTotalCommitted()
    {
        var table = MakeTable(currentBet: 20, pot: 20);
        var seat = MakeSeat(1, stack: 200, currentBet: 0, totalCommitted: 10);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Call));
        Assert.Equal(30, seat.TotalCommitted);
    }

    [Fact]
    public void Apply_Call_ShortStack_GoesAllIn()
    {
        var table = MakeTable(currentBet: 100, pot: 100);
        var seat = MakeSeat(1, stack: 40, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Call));
        Assert.Equal(0, seat.Stack);
        Assert.Equal(PokerSeatStatus.AllIn, seat.Status);
    }

    [Fact]
    public void Apply_AllIn_ZeroesStack()
    {
        var table = MakeTable(currentBet: 0, pot: 0);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.AllIn));
        Assert.Equal(0, seat.Stack);
    }

    [Fact]
    public void Apply_AllIn_SetsStatusAllIn()
    {
        var table = MakeTable(currentBet: 0);
        var seat = MakeSeat(1, stack: 200);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.AllIn));
        Assert.Equal(PokerSeatStatus.AllIn, seat.Status);
    }

    [Fact]
    public void Apply_AllIn_UpdatesTableCurrentBetWhenHigher()
    {
        var table = MakeTable(currentBet: 50, pot: 50);
        var seat = MakeSeat(1, stack: 200, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.AllIn));
        Assert.Equal(200, table.CurrentBet);
    }

    [Fact]
    public void Apply_Raise_ReducesStackByPut()
    {
        var table = MakeTable(currentBet: 10, pot: 10, minRaise: 10);
        var seat = MakeSeat(1, stack: 500, currentBet: 0);
        // Raise to 30: put = 30 - 0 = 30
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Raise, 30));
        Assert.Equal(470, seat.Stack);
    }

    [Fact]
    public void Apply_Raise_UpdatesSeatCurrentBet()
    {
        var table = MakeTable(currentBet: 10, pot: 10);
        var seat = MakeSeat(1, stack: 500, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Raise, 30));
        Assert.Equal(30, seat.CurrentBet);
    }

    [Fact]
    public void Apply_Raise_UpdatesTableCurrentBet()
    {
        var table = MakeTable(currentBet: 10, pot: 10);
        var seat = MakeSeat(1, stack: 500, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Raise, 30));
        Assert.Equal(30, table.CurrentBet);
    }

    [Fact]
    public void Apply_Raise_AddsToPot()
    {
        var table = MakeTable(currentBet: 10, pot: 10);
        var seat = MakeSeat(1, stack: 500, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Raise, 30));
        Assert.Equal(40, table.Pot); // initial 10 + raise put of 30
    }

    [Fact]
    public void Apply_Raise_ToExactStack_SetsAllIn()
    {
        var table = MakeTable(currentBet: 10, pot: 10);
        var seat = MakeSeat(1, stack: 30, currentBet: 0);
        PokerDomain.Apply(table, seat, new PokerAction(PokerActionKind.Raise, 30));
        Assert.Equal(PokerSeatStatus.AllIn, seat.Status);
    }

    // ── ResolveAfterAction ───────────────────────────────────────────────────

    [Fact]
    public void ResolveAfterAction_TurnAdvanced_WhenNotAllActed()
    {
        var table = MakeTable(currentBet: 10, phase: PokerPhase.PreFlop);
        table.CurrentSeat = 1;
        var s1 = MakeSeat(1, currentBet: 10, hasActed: true);
        var s2 = MakeSeat(2, currentBet: 0, hasActed: false);
        var seats = new List<PokerSeat> { s1, s2 };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.TurnAdvanced, t.Kind);
        Assert.Equal(2, table.CurrentSeat);
    }

    [Fact]
    public void ResolveAfterAction_PhaseAdvanced_WhenBettingRoundComplete()
    {
        var table = MakeTable(currentBet: 10, phase: PokerPhase.PreFlop, pot: 20);
        var s1 = MakeSeat(1, currentBet: 10, hasActed: true);
        var s2 = MakeSeat(2, currentBet: 10, hasActed: true);
        var seats = new List<PokerSeat> { s1, s2 };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.PhaseAdvanced, t.Kind);
        Assert.Equal(PokerPhase.PreFlop, t.FromPhase);
        Assert.Equal(PokerPhase.Flop, t.ToPhase);
    }

    [Fact]
    public void ResolveAfterAction_PhaseAdvanced_FlopToTurn()
    {
        var table = MakeTable(currentBet: 0, phase: PokerPhase.Flop, pot: 20,
            deck: "2S 3S 4S 5S 6S 7S 8S 9S TS JS");
        table.CommunityCards = "KH QH JH";
        var s1 = MakeSeat(1, currentBet: 0, hasActed: true);
        var s2 = MakeSeat(2, currentBet: 0, hasActed: true);
        var seats = new List<PokerSeat> { s1, s2 };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.PhaseAdvanced, t.Kind);
        Assert.Equal(PokerPhase.Turn, t.ToPhase);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedLastStanding_WhenOneFolds()
    {
        var table = MakeTable(pot: 100, phase: PokerPhase.PreFlop);
        var winner = MakeSeat(1, stack: 490, currentBet: 10, holeCards: "AS KS");
        var loser = MakeSeat(2, status: PokerSeatStatus.Folded, holeCards: "");
        var seats = new List<PokerSeat> { winner, loser };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.HandEndedLastStanding, t.Kind);
        Assert.Equal(590, winner.Stack); // 490 + 100 pot
        Assert.Equal(0, table.Pot);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedLastStanding_ShowdownContainsWinner()
    {
        var table = MakeTable(pot: 60, phase: PokerPhase.PreFlop);
        var winner = MakeSeat(1, stack: 200, holeCards: "AS KS");
        var loser = MakeSeat(2, status: PokerSeatStatus.Folded);
        var seats = new List<PokerSeat> { winner, loser };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.NotNull(t.Showdown);
        Assert.Single(t.Showdown!);
        Assert.Equal(winner.UserId, t.Showdown![0].Seat.UserId);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedRunout_WhenAllAllIn()
    {
        // Both players all-in → runout community cards and settle
        var table = MakeTable(pot: 200, phase: PokerPhase.PreFlop,
            deck: "2S 3S 4S 5S 6S 7S 8S 9S TS JS QS KS");
        table.CommunityCards = "";
        var s1 = MakeSeat(1, stack: 0, status: PokerSeatStatus.AllIn, holeCards: "AS AD");
        var s2 = MakeSeat(2, stack: 0, status: PokerSeatStatus.AllIn, holeCards: "2H 3H");
        var seats = new List<PokerSeat> { s1, s2 };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.HandEndedRunout, t.Kind);
        Assert.NotNull(t.Showdown);
        Assert.Equal(0, table.Pot);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedShowdown_WhenRiverBettingDone()
    {
        // River complete → showdown settle
        var table = MakeTable(pot: 100, phase: PokerPhase.River, currentBet: 0);
        table.CommunityCards = "KH KD KC 2C 3C";
        var s1 = MakeSeat(1, stack: 200, currentBet: 0, hasActed: true, holeCards: "AS AD");
        var s2 = MakeSeat(2, stack: 200, currentBet: 0, hasActed: true, holeCards: "7H 8H");
        var seats = new List<PokerSeat> { s1, s2 };

        var t = PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(TransitionKind.HandEndedShowdown, t.Kind);
        Assert.NotNull(t.Showdown);
        Assert.Equal(0, table.Pot);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedShowdown_BestHandWinsPot()
    {
        // s1 has KKKAA (full house) vs s2 has KKKJ8 (trips) — s1 should win
        var table = MakeTable(pot: 100, phase: PokerPhase.River, currentBet: 0);
        table.CommunityCards = "KH KD KC 2C 3C";
        var s1 = MakeSeat(1, stack: 0, currentBet: 0, hasActed: true,
            status: PokerSeatStatus.Seated, holeCards: "AS AD");
        var s2 = MakeSeat(2, stack: 0, currentBet: 0, hasActed: true,
            status: PokerSeatStatus.Seated, holeCards: "JH 8H");
        var seats = new List<PokerSeat> { s1, s2 };

        PokerDomain.ResolveAfterAction(table, seats);

        Assert.True(s1.Stack > 0, "Full house should beat trips");
        Assert.Equal(0, s2.Stack);
    }

    [Fact]
    public void ResolveAfterAction_HandEndedShowdown_SettlesSidePots()
    {
        var table = MakeTable(pot: 300, phase: PokerPhase.River, currentBet: 0);
        table.CommunityCards = "2C 7D 9H JS QC";
        var shortWinner = MakeSeat(1, stack: 0, currentBet: 0, hasActed: true,
            status: PokerSeatStatus.AllIn, holeCards: "AS AD", totalCommitted: 50);
        var bigStackWinner = MakeSeat(2, stack: 0, currentBet: 0, hasActed: true,
            status: PokerSeatStatus.Seated, holeCards: "KH KD", totalCommitted: 125);
        var caller = MakeSeat(3, stack: 0, currentBet: 0, hasActed: true,
            status: PokerSeatStatus.Seated, holeCards: "3S 4D", totalCommitted: 125);
        var seats = new List<PokerSeat> { shortWinner, bigStackWinner, caller };

        PokerDomain.ResolveAfterAction(table, seats);

        Assert.Equal(150, shortWinner.Stack);
        Assert.Equal(150, bigStackWinner.Stack);
        Assert.Equal(0, caller.Stack);
    }

    [Fact]
    public void ResolveAfterAction_RaiseResetsHasActed_ForSeatedPlayersBelow()
    {
        // s2 had acted, but s1 raises above s2's current bet — s2 must act again
        var table = MakeTable(currentBet: 30, phase: PokerPhase.PreFlop, pot: 40);
        table.CurrentSeat = 1;
        var s1 = MakeSeat(1, stack: 470, currentBet: 30, hasActed: true);
        var s2 = MakeSeat(2, stack: 480, currentBet: 10, hasActed: true); // below currentBet
        var seats = new List<PokerSeat> { s1, s2 };

        PokerDomain.ResolveAfterAction(table, seats);

        Assert.False(s2.HasActedThisRound);
    }

    // ── DecideAutoAction ─────────────────────────────────────────────────────

    [Fact]
    public void DecideAutoAction_NoBet_ReturnsCheck()
    {
        var table = MakeTable(currentBet: 0);
        var seat = MakeSeat(1, currentBet: 0);
        var action = PokerDomain.DecideAutoAction(table, seat);
        Assert.Equal(PokerActionKind.Check, action.Kind);
    }

    [Fact]
    public void DecideAutoAction_BetOutstanding_ReturnsFold()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(1, currentBet: 0);
        var action = PokerDomain.DecideAutoAction(table, seat);
        Assert.Equal(PokerActionKind.Fold, action.Kind);
    }

    [Fact]
    public void DecideAutoAction_SeatMatchesCurrentBet_ReturnsCheck()
    {
        var table = MakeTable(currentBet: 20);
        var seat = MakeSeat(1, currentBet: 20);
        var action = PokerDomain.DecideAutoAction(table, seat);
        Assert.Equal(PokerActionKind.Check, action.Kind);
    }
}
