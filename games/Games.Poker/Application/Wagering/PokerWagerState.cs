namespace Games.Poker.Application.Wagering;

/// <summary>
/// Poker's outcome-only table state. Stack, pot and committed bets are game
/// mechanics; stake, balance and payout live in Wagering/Ledger.
/// </summary>
public sealed record PokerWagerState(
    PokerTable? Table,
    List<PokerSeat> Seats);
