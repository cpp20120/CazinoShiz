namespace Games.Blackjack;

public enum BlackjackError
{
    None = 0,
    InvalidBet,
    NotEnoughCoins,
    HandInProgress,
    NoActiveHand,
    CannotDouble,
}

public enum BlackjackOutcome
{
    PlayerBust,
    DealerBust,
    PlayerWin,
    DealerWin,
    Push,
    PlayerBlackjack,
}

public sealed record BlackjackSnapshot(
    string[] PlayerCards,
    string[] DealerCards,
    int PlayerTotal,
    int DealerTotal,
    int Bet,
    int PlayerCoins,
    bool DealerHoleRevealed,
    bool CanDouble,
    BlackjackOutcome? Outcome,
    int Payout);

public sealed record BlackjackResult(BlackjackError Error, BlackjackSnapshot? Snapshot, int? StateMessageId = null);
