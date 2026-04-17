namespace CasinoShiz.Services.Poker.Presentation;

public abstract record PokerCommand
{
    public sealed record Usage : PokerCommand;
    public sealed record Unknown(string Action) : PokerCommand;
    public sealed record Create : PokerCommand;
    public sealed record Join(string Code) : PokerCommand;
    public sealed record JoinMissingCode : PokerCommand;
    public sealed record Start : PokerCommand;
    public sealed record Leave : PokerCommand;
    public sealed record Status : PokerCommand;
    public sealed record Raise(int Amount) : PokerCommand;
    public sealed record RaiseMissingAmount : PokerCommand;

    public sealed record PlayerAction(string Action, int Amount) : PokerCommand;
    public sealed record RaiseMenu : PokerCommand;
}
