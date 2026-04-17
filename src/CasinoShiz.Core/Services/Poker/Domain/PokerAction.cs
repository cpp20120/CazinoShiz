namespace CasinoShiz.Services.Poker.Domain;

public enum PokerActionKind
{
    Check,
    Call,
    Fold,
    AllIn,
    Raise,
}

public enum AutoAction
{
    Check = 0,
    Fold = 1,
}

public readonly record struct PokerAction(PokerActionKind Kind, int Amount = 0)
{
    public static PokerAction Check() => new(PokerActionKind.Check);
    private static PokerAction Call() => new(PokerActionKind.Call);
    public static PokerAction Fold() => new(PokerActionKind.Fold);
    private static PokerAction AllIn() => new(PokerActionKind.AllIn);
    private static PokerAction Raise(int to) => new(PokerActionKind.Raise, to);

    public static PokerAction? FromVerb(string verb, int amount) => verb switch
    {
        "check" => Check(),
        "call" => Call(),
        "fold" => Fold(),
        "allin" => AllIn(),
        "raise" => Raise(amount),
        _ => null,
    };
}
