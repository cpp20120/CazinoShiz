using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Horse;
using CasinoShiz.Services.Poker.Application;

namespace CasinoShiz.Services.Admin;

public sealed record PayResult(string DisplayName, int OldCoins, int NewCoins, int Amount);

public sealed record UserLookup(UserState? User);

public enum RenameOp { Set, Cleared, NoChange }
public sealed record RenameResult(RenameOp Op, string OldName, string NewName);

public sealed record UserListItem(long TelegramUserId, string DisplayName, int Coins, long LastDayUtc, int AttemptCount, int ExtraAttempts);

public sealed record UserListResult(IReadOnlyList<UserListItem> Users, int Total, int Skip, int Take);

public sealed record UserDetail(
    UserState User,
    IReadOnlyList<HorseBet> RecentBets,
    IReadOnlyList<FreespinCode> IssuedCodes,
    PokerSeat? PokerSeat,
    BlackjackHand? BlackjackHand);

public sealed record OverviewStats(
    int TotalUsers,
    int ActivePokerTables,
    int ActivePokerPlayers,
    int ActiveBlackjackHands,
    long TotalBlackjackHandsPlayed,
    int HorseBetsToday,
    int HorsePotToday,
    int HorseRacesRun,
    int DiceAttemptsToday,
    int ActiveFreespinCodes,
    int CubePendingBets,
    int CubePendingPot,
    int DartsPendingBets,
    int DartsPendingPot);

public sealed record HorseRaceAdminView(
    string RaceDate,
    int BetsCount,
    int MinBetsToRun,
    Dictionary<int, int> Stakes,
    Dictionary<int, double> Koefs,
    HorseResult? LastResult);

public sealed record HorseRunAdminResult(
    HorseError Error,
    int? Winner,
    List<(long UserId, int Amount)> Winners,
    bool BroadcastedToChannel,
    byte[]? GifBytes = null);

public enum AdminCancelOp { Done, Noop }
public sealed record CancelResult(AdminCancelOp Op, int Refunded);
public sealed record PokerKickResult(AdminCancelOp Op, int Refunded, TableSnapshot? RemainingSnapshot);
