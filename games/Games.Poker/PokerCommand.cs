namespace Games.Poker;

public abstract record PokerCommand
{
    public sealed record Usage : PokerCommand;
    public sealed record Unknown(string Action) : PokerCommand;
    public sealed record Create : PokerCommand;
    public sealed record Join(string Code) : PokerCommand;
    public sealed record JoinCurrent : PokerCommand;
    public sealed record JoinMissingCode : PokerCommand;
    public sealed record Start : PokerCommand;
    public sealed record Leave : PokerCommand;
    public sealed record Status : PokerCommand;
    public sealed record Raise(int Amount) : PokerCommand;
    public sealed record RaiseMissingAmount : PokerCommand;

    public sealed record PlayerAction(string Action, int Amount, long? ExpectedUserId) : PokerCommand;
    public sealed record RaiseMenu(long? ExpectedUserId) : PokerCommand;
    public sealed record ShowCards : PokerCommand;
}

public static class PokerCommandParser
{
    public static PokerCommand ParseText(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";

        return verb switch
        {
            "" => new PokerCommand.Usage(),
            "create" => new PokerCommand.Create(),
            "join" => parts.Length > 2
                ? new PokerCommand.Join(parts[2].ToUpperInvariant())
                : new PokerCommand.JoinCurrent(),
            "start" => new PokerCommand.Start(),
            "leave" => new PokerCommand.Leave(),
            "status" => new PokerCommand.Status(),
            "raise" => parts.Length > 2 && int.TryParse(parts[2], out int amt)
                ? new PokerCommand.Raise(amt)
                : new PokerCommand.RaiseMissingAmount(),
            _ => new PokerCommand.Unknown(verb),
        };
    }

    public static PokerCommand? ParseCallback(string? data)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith("poker:")) return null;
        var tokens = data.Split(':');
        var verb = tokens.Length > 1 ? tokens[1] : "";

        return verb switch
        {
            "check" or "call" or "fold" or "allin" => new PokerCommand.PlayerAction(verb, 0, ParseUserId(tokens, 2)),
            "raise" when tokens.Length > 2 && int.TryParse(tokens[2], out int amt)
                => new PokerCommand.PlayerAction("raise", amt, ParseUserId(tokens, 3)),
            "raise_menu" => new PokerCommand.RaiseMenu(ParseUserId(tokens, 2)),
            "join" => new PokerCommand.JoinCurrent(),
            "start" => new PokerCommand.Start(),
            "cards" => new PokerCommand.ShowCards(),
            _ => null,
        };
    }

    private static long? ParseUserId(string[] tokens, int index) =>
        tokens.Length > index && long.TryParse(tokens[index], out var userId) ? userId : null;
}
