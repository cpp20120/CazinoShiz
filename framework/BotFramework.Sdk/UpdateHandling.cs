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
//   [Command("/foo")]        100 + prefix.Length   (longer prefix wins ties)
//   [CallbackFallback]         1
//
// A handler decorated with [Command("/poker")] always beats [Command("/p")]
// because its prefix is longer. CallbackFallback sits at the bottom so it
// catches only callback queries no other handler matched.
// ─────────────────────────────────────────────────────────────────────────────

using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFramework.Sdk;

/// A handler processes one Telegram update. Handlers are resolved from DI on
/// every dispatch — make them Scoped unless they're stateless.
public interface IUpdateHandler
{
    Task HandleAsync(UpdateContext ctx);
}

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
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class RouteAttribute : Attribute
{
    public abstract int Priority { get; }
    public abstract string Name { get; }
    public abstract bool Matches(Update update);
}

public sealed class CommandAttribute(string prefix) : RouteAttribute
{
    public string Prefix { get; } = prefix;
    public override int Priority => 100 + Prefix.Length;
    public override string Name => $"cmd:{Prefix}";
    public override bool Matches(Update update) =>
        update.Message?.Text is { } text && text.StartsWith(Prefix);
}

public sealed class CallbackPrefixAttribute(string prefix) : RouteAttribute
{
    public string Prefix { get; } = prefix;
    public override int Priority => 200;
    public override string Name => $"cb:{Prefix}";
    public override bool Matches(Update update) =>
        update.CallbackQuery?.Data?.StartsWith(Prefix) == true;
}

public sealed class MessageDiceAttribute(string emoji) : RouteAttribute
{
    public string Emoji { get; } = emoji;
    public override int Priority => 250;
    public override string Name => $"dice:{Emoji}";
    public override bool Matches(Update update) =>
        update.Message?.Dice?.Emoji == Emoji;
}

public sealed class ChannelPostAttribute : RouteAttribute
{
    public override int Priority => 300;
    public override string Name => "channel_post";
    public override bool Matches(Update update) => update.ChannelPost != null;
}

public sealed class CallbackFallbackAttribute : RouteAttribute
{
    public override int Priority => 1;
    public override string Name => "cb_fallback";
    public override bool Matches(Update update) => update.CallbackQuery != null;
}
