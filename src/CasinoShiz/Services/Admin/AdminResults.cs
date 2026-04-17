using CasinoShiz.Data.Entities;

namespace CasinoShiz.Services.Admin;

public sealed record PayResult(string DisplayName, int OldCoins, int NewCoins, int Amount);

public sealed record UserLookup(UserState? User);

public enum RenameOp { Set, Cleared, NoChange }
public sealed record RenameResult(RenameOp Op, string OldName, string NewName);
