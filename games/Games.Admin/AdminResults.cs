namespace Games.Admin;

public sealed record UserSummary(
    long TelegramUserId, long BalanceScopeId, string DisplayName, int Coins, long UpdatedAtUnixMs);

public sealed record PayResult(string DisplayName, int OldCoins, int NewCoins, int Amount);

public enum RenameOp { Set, Cleared, NoChange }
public sealed record RenameResult(RenameOp Op, string OldName, string NewName);
