using CasinoShiz.Data.Entities;

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
    PokerSeat? PokerSeat);
