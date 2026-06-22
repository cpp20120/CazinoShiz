// ─────────────────────────────────────────────────────────────────────────────
// Update-handling surface for modules.
//
// A handler is a class implementing IUpdateHandler, decorated with one or more
// RouteAttribute subclasses that declare WHICH Telegram updates it wants. The
// Host-side UpdateRouter reflects over every loaded module assembly, builds a
// priority-ordered route table at startup, and dispatches each incoming update
// to the first matching handler.
//
// Priorities (same as the live bot — modules are a drop-in for existing code):
//   [ChannelPost]            300
//   [MessageDice("🎰")]      250
//   [CallbackPrefix("xyz")]  200
//   [Command("/foo")]        100 + prefix.Length   (longer prefix wins ties; also matches plain "foo")
//   [TextCommand("foo")]      90 + token.Length    (plain text fallback / aliases)
//   [CallbackFallback]         1
//
// A handler decorated with [Command("/poker")] always beats [Command("/p")]
// because its prefix is longer. CallbackFallback sits at the bottom so it
// catches only callback queries no other handler matched.
// ─────────────────────────────────────────────────────────────────────────────

using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFramework.Sdk;
public sealed class CommandAttribute(string prefix) : RouteAttribute
{
    public string Prefix { get; } = prefix;
    public override int Priority => 100 + Prefix.Length;
    public override string Name => $"cmd:{Prefix}";
    public override bool Matches(Update update) =>
        TryGetCommandToken(update.Message?.Text) is { } commandToken
        && (string.Equals(commandToken, Prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandToken, Prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase));

    private static string? TryGetCommandToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var span = text.AsSpan().TrimStart();
        if (span.IsEmpty)
            return null;

        var spaceIndex = span.IndexOf(' ');
        var token = spaceIndex >= 0 ? span[..spaceIndex] : span;
        if (token.IsEmpty)
            return null;

        var mentionIndex = token.IndexOf('@');
        if (mentionIndex >= 0)
            token = token[..mentionIndex];

        if (!token.IsEmpty && token[0] == '/')
            token = token[1..];

        return token.IsEmpty ? null : token.ToString();
    }
}
