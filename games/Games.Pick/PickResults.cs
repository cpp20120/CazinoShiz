namespace Games.Pick;

public enum PickError
{
    None,
    InvalidAmount,
    NotEnoughVariants,
    TooManyVariants,
    InvalidChoice,
    NotEnoughCoins,
    ChainExpired,
    ChainBusy,
}

/// <summary>
/// Outcome of one <c>/pick</c> roll.
/// <list type="bullet">
///   <item><see cref="Variants"/> is the original list (in order).</item>
///   <item><see cref="BackedIndices"/> are 0-based indices of the variants the player backed.</item>
///   <item><see cref="PickedIndex"/> is the index the bot rolled (-1 if validation failed).</item>
///   <item><see cref="Payout"/> is what was credited (0 on loss). <see cref="Net"/> is signed.</item>
///   <item><see cref="StreakBefore"/> / <see cref="StreakAfter"/> let the UI render the streak.</item>
///   <item><see cref="ChainGuid"/> is non-null when the player won and another chain hop is offered.</item>
/// </list>
/// </summary>
public sealed record PickResult(
    PickError Error,
    int Bet,
    int Balance,
    int Payout,
    int Net,
    int StreakBonus,
    int StreakBefore,
    int StreakAfter,
    int PickedIndex,
    bool Won,
    int ChainDepth,
    Guid? ChainGuid,
    IReadOnlyList<string> Variants,
    IReadOnlyList<int> BackedIndices);
