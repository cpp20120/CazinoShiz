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
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class RouteAttribute : Attribute
{
    public abstract int Priority { get; }
    public abstract string Name { get; }
    public abstract bool Matches(Update update);
}
