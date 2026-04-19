namespace Games.SecretHitler;

public abstract record SecretHitlerCommand
{
    public sealed record Usage : SecretHitlerCommand;
    public sealed record Unknown(string Action) : SecretHitlerCommand;
    public sealed record Create : SecretHitlerCommand;
    public sealed record Join(string Code) : SecretHitlerCommand;
    public sealed record JoinMissingCode : SecretHitlerCommand;
    public sealed record Start : SecretHitlerCommand;
    public sealed record Leave : SecretHitlerCommand;
    public sealed record Status : SecretHitlerCommand;

    public sealed record Nominate(int ChancellorPosition) : SecretHitlerCommand;
    public sealed record Vote(bool Ja) : SecretHitlerCommand;
    public sealed record PresidentDiscard(int Index) : SecretHitlerCommand;
    public sealed record ChancellorEnact(int Index) : SecretHitlerCommand;
}

public static class SecretHitlerCommandParser
{
    public static SecretHitlerCommand ParseText(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";

        return verb switch
        {
            "" => new SecretHitlerCommand.Usage(),
            "create" => new SecretHitlerCommand.Create(),
            "join" => parts.Length > 2
                ? new SecretHitlerCommand.Join(parts[2].ToUpperInvariant())
                : new SecretHitlerCommand.JoinMissingCode(),
            "start" => new SecretHitlerCommand.Start(),
            "leave" => new SecretHitlerCommand.Leave(),
            "status" => new SecretHitlerCommand.Status(),
            _ => new SecretHitlerCommand.Unknown(verb),
        };
    }

    public static SecretHitlerCommand? ParseCallback(string? data)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith("sh:")) return null;
        var tokens = data.Split(':');
        var verb = tokens.Length > 1 ? tokens[1] : "";

        return verb switch
        {
            "nominate" when tokens.Length > 2 && int.TryParse(tokens[2], out int pos)
                => new SecretHitlerCommand.Nominate(pos),
            "vote" when tokens.Length > 2 => tokens[2] switch
            {
                "ja" => new SecretHitlerCommand.Vote(true),
                "nein" => new SecretHitlerCommand.Vote(false),
                _ => null,
            },
            "discard" when tokens.Length > 2 && int.TryParse(tokens[2], out int idx)
                => new SecretHitlerCommand.PresidentDiscard(idx),
            "enact" when tokens.Length > 2 && int.TryParse(tokens[2], out int idx)
                => new SecretHitlerCommand.ChancellorEnact(idx),
            _ => null,
        };
    }
}
