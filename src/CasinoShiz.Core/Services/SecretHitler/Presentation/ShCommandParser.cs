namespace CasinoShiz.Services.SecretHitler.Presentation;

public static class ShCommandParser
{
    public static ShCommand ParseText(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";

        return verb switch
        {
            "" => new ShCommand.Usage(),
            "create" => new ShCommand.Create(),
            "join" => parts.Length > 2 ? new ShCommand.Join(parts[2].ToUpperInvariant()) : new ShCommand.JoinMissingCode(),
            "start" => new ShCommand.Start(),
            "leave" => new ShCommand.Leave(),
            "status" => new ShCommand.Status(),
            _ => new ShCommand.Unknown(verb),
        };
    }

    public static ShCommand? ParseCallback(string? data)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith("sh:")) return null;
        var tokens = data.Split(':');
        var verb = tokens.Length > 1 ? tokens[1] : "";

        return verb switch
        {
            "nominate_menu" => new ShCommand.NominateMenu(),
            "nominate" when tokens.Length > 2 && int.TryParse(tokens[2], out int pos) => new ShCommand.Nominate(pos),
            "vote" when tokens.Length > 2 => tokens[2] switch
            {
                "ja" => new ShCommand.Vote(true),
                "nein" => new ShCommand.Vote(false),
                _ => null,
            },
            "discard" when tokens.Length > 2 && int.TryParse(tokens[2], out int idx) => new ShCommand.PresidentDiscard(idx),
            "enact" when tokens.Length > 2 && int.TryParse(tokens[2], out int idx) => new ShCommand.ChancellorEnact(idx),
            _ => null,
        };
    }
}
