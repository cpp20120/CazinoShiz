using Games.Blackjack;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

// Tests for BlackjackService settle/resolve paths using injected deterministic hands.
// Card notation: RankSuit — e.g. "KS" = King of Spades, "AS" = Ace of Spades.
// Dealer hits until total >= 17 (hits on soft 17).
public class BlackjackResolveTests
{
    private static BlackjackService MakeService(
        FakeEconomicsService? economics = null,
        InMemoryBlackjackHandStore? hands = null) =>
        new(
            hands ?? new InMemoryBlackjackHandStore(),
            economics ?? new FakeEconomicsService(),
            new NullAnalyticsService(),
            new NullEventBus(),
            Options.Create(new BlackjackOptions { MinBet = 1, MaxBet = 1_000 }));

    private static BlackjackHandRow MakeHand(
        string playerCards, string dealerCards, string deckState, int bet = 10) =>
        new(UserId: 1, ChatId: 100, Bet: bet,
            PlayerCards: playerCards, DealerCards: dealerCards,
            DeckState: deckState, StateMessageId: null,
            CreatedAt: DateTimeOffset.UtcNow);

    // ── StandAsync outcomes ─────────────────────────────────────────────────

    [Fact]
    public async Task StandAsync_PlayerWins_ReturnsPlayerWin()
    {
        // Player 20 vs dealer 13; deck has "4S" so dealer hits to 17 → player wins
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS KH", "8S 5H", "4S 2C 3D 6H 7S 9C"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.StandAsync(1, default);

        Assert.Equal(BlackjackError.None, result.Error);
        Assert.Equal(BlackjackOutcome.PlayerWin, result.Snapshot!.Outcome);
        Assert.Equal(20, result.Snapshot!.Payout); // bet=10, payout=bet*2
    }

    [Fact]
    public async Task StandAsync_PlayerWins_CreditsPayout()
    {
        var econ = new FakeEconomicsService();
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS KH", "8S 5H", "4S 2C 3D 6H 7S 9C"), default);
        var svc = MakeService(economics: econ, hands: hands);

        await svc.StandAsync(1, default);

        Assert.Single(econ.Credits);
        Assert.Equal(20, econ.Credits[0].Amount);
    }

    [Fact]
    public async Task StandAsync_DealerBust_ReturnsDealerBust()
    {
        // Player 20 vs dealer 16; deck has "KD" so dealer hits to 26 → dealer busts
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS KH", "9S 7H", "KD 2C 3D 4H 5S 6C"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.StandAsync(1, default);

        Assert.Equal(BlackjackOutcome.DealerBust, result.Snapshot!.Outcome);
        Assert.Equal(20, result.Snapshot!.Payout);
    }

    [Fact]
    public async Task StandAsync_Push_ReturnsPush()
    {
        // Player 20 vs dealer 20; dealer already at 20 (>= 17), no draw needed → push
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS QH", "KD JH", "2S 3C 4D 5H 6S 7C"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.StandAsync(1, default);

        Assert.Equal(BlackjackOutcome.Push, result.Snapshot!.Outcome);
        Assert.Equal(10, result.Snapshot!.Payout); // push returns bet
    }

    [Fact]
    public async Task StandAsync_Push_CreditsBetBack()
    {
        var econ = new FakeEconomicsService();
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS QH", "KD JH", "2S 3C 4D 5H 6S 7C"), default);
        var svc = MakeService(economics: econ, hands: hands);

        await svc.StandAsync(1, default);

        Assert.Single(econ.Credits);
        Assert.Equal(10, econ.Credits[0].Amount);
    }

    [Fact]
    public async Task StandAsync_DealerWins_ReturnsDealerWin()
    {
        // Player 15 vs dealer 16; deck has "5S" so dealer hits to 21 → dealer wins
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("7S 8H", "KD 6H", "5S 2C 3D 4H 9S JC"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.StandAsync(1, default);

        Assert.Equal(BlackjackOutcome.DealerWin, result.Snapshot!.Outcome);
        Assert.Equal(0, result.Snapshot!.Payout);
    }

    [Fact]
    public async Task StandAsync_DealerWins_NoCredit()
    {
        var econ = new FakeEconomicsService();
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("7S 8H", "KD 6H", "5S 2C 3D 4H 9S JC"), default);
        var svc = MakeService(economics: econ, hands: hands);

        await svc.StandAsync(1, default);

        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task StandAsync_Settles_DeletesHand()
    {
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS KH", "8S 5H", "4S 2C 3D 6H 7S 9C"), default);
        var svc = MakeService(hands: hands);

        await svc.StandAsync(1, default);

        var (snapshot, _) = await svc.GetSnapshotAsync(1, default);
        Assert.Null(snapshot);
    }

    // ── HitAsync outcomes ───────────────────────────────────────────────────

    [Fact]
    public async Task HitAsync_PlayerBust_ReturnsPlayerBust()
    {
        // Player 16; deck starts with KD (10) → 26 → bust
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("9S 7H", "8D 5C", "KD 2C 3D 4H 5S 6C"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.HitAsync(1, default);

        Assert.Equal(BlackjackOutcome.PlayerBust, result.Snapshot!.Outcome);
        Assert.Equal(0, result.Snapshot!.Payout);
    }

    [Fact]
    public async Task HitAsync_PlayerBust_NoCredit()
    {
        var econ = new FakeEconomicsService();
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("9S 7H", "8D 5C", "KD 2C 3D 4H 5S 6C"), default);
        var svc = MakeService(economics: econ, hands: hands);

        await svc.HitAsync(1, default);

        Assert.Empty(econ.Credits);
    }

    [Fact]
    public async Task HitAsync_NoBust_HandRemainsActive()
    {
        // Player 11; deck starts with 5S (5) → 16, no bust
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("6S 5H", "8D 7C", "5S 2C 3D 4H 9S JC"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.HitAsync(1, default);

        Assert.Equal(BlackjackError.None, result.Error);
        Assert.Null(result.Snapshot!.Outcome); // still in progress
    }

    // ── DoubleAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DoubleAsync_MoreThanTwoCards_ReturnsCannotDouble()
    {
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("5S 6H 3D", "8D 5C", "KD 2C 3D 4H 5S 6C"), default);
        var svc = MakeService(hands: hands);

        var result = await svc.DoubleAsync(1, default);

        Assert.Equal(BlackjackError.CannotDouble, result.Error);
    }

    [Fact]
    public async Task DoubleAsync_InsufficientFunds_ReturnsNotEnoughCoins()
    {
        var econ = new FakeEconomicsService { StartingBalance = 5 };
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("5S 6H", "8D 5C", "KD 2C 3D 4H 5S 6C", bet: 100), default);
        var svc = MakeService(economics: econ, hands: hands);

        var result = await svc.DoubleAsync(1, default);

        Assert.Equal(BlackjackError.NotEnoughCoins, result.Error);
    }

    [Fact]
    public async Task DoubleAsync_Success_DoublesTheBet()
    {
        // Player 11; deck "TS KD ...": player draws TS → 21; dealer 16 draws KD → bust
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("5S 6H", "9S 7H", "TS KD 2C 3D 4H 5C", bet: 10), default);
        var svc = MakeService(economics: econ, hands: hands);

        var result = await svc.DoubleAsync(1, default);

        Assert.Equal(BlackjackError.None, result.Error);
        Assert.Equal(20, result.Snapshot!.Bet); // original 10, doubled to 20
    }

    [Fact]
    public async Task DoubleAsync_Success_DebitsDoubleAmount()
    {
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("5S 6H", "9S 7H", "TS KD 2C 3D 4H 5C", bet: 10), default);
        var svc = MakeService(economics: econ, hands: hands);

        await svc.DoubleAsync(1, default);

        // One debit for the double (the initial bet debit happened at StartAsync time)
        Assert.Single(econ.Debits);
        Assert.Equal(10, econ.Debits[0].Amount);
    }

    [Fact]
    public async Task DoubleAsync_DealerBust_CreditsTwiceDoubledBet()
    {
        // Player draws TS → 21; dealer 16 draws KD → bust; payout = doubled_bet * 2 = 40
        var econ = new FakeEconomicsService { StartingBalance = 1_000 };
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("5S 6H", "9S 7H", "TS KD 2C 3D 4H 5C", bet: 10), default);
        var svc = MakeService(economics: econ, hands: hands);

        var result = await svc.DoubleAsync(1, default);

        Assert.Single(econ.Credits);
        Assert.Equal(40, econ.Credits[0].Amount); // bet=20 (doubled) * 2 for DealerBust
    }

    // ── Resolve payout formula ───────────────────────────────────────────────

    [Theory]
    [InlineData(10, 20)]  // bet=10, PlayerWin → payout = bet*2 = 20
    [InlineData(50, 100)] // bet=50, PlayerWin → payout = bet*2 = 100
    public async Task StandAsync_PlayerWin_PayoutIsTwiceBet(int bet, int expectedPayout)
    {
        var hands = new InMemoryBlackjackHandStore();
        await hands.InsertAsync(MakeHand("KS KH", "8S 5H", "4S 2C 3D 6H 7S 9C", bet: bet), default);
        var svc = MakeService(hands: hands);

        var result = await svc.StandAsync(1, default);

        Assert.Equal(expectedPayout, result.Snapshot!.Payout);
    }
}
