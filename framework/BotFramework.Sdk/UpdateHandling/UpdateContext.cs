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
/// Request-scoped context carrying the Telegram update, the bot client, and
/// the scoped service provider. Handlers read whatever they need from here.
public sealed class UpdateContext(
    ITelegramBotClient bot,
    Update update,
    IServiceProvider services,
    CancellationToken ct)
{
    public ITelegramBotClient Bot { get; } = bot;
    public Update Update { get; } = update;
    public IServiceProvider Services { get; } = services;
    public CancellationToken Ct { get; } = ct;

    /// Per-request bag for middleware to pass data down the chain.
    public Dictionary<string, object> Items { get; } = new();

    public long UserId =>
        Update.Message?.From?.Id
        ?? Update.CallbackQuery?.From.Id
        ?? Update.ChannelPost?.From?.Id
        ?? 0;

    public long ChatId =>
        Update.Message?.Chat.Id
        ?? Update.CallbackQuery?.Message?.Chat.Id
        ?? Update.ChannelPost?.Chat.Id
        ?? 0;

    public string? Text => Update.Message?.Text;
    public string? CallbackData => Update.CallbackQuery?.Data;

    /// <summary>Regular or edited chat message (dice finals may arrive as <see cref="Update.EditedMessage"/>).</summary>
    public Message? MessageOrEdited => Update.Message ?? Update.EditedMessage;
}
