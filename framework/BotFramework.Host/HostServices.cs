// ─────────────────────────────────────────────────────────────────────────────
// Host-supplied cross-cutting services. Every module depends on these; they do
// NOT belong to any single game.
//
// Keeping them in the Host (and their interfaces in the SDK) means:
//   • one economics ledger across every game — slots debits the same balance
//     blackjack credits
//   • one analytics sink — a module just calls IAnalytics.Track(moduleId, ...)
//     and the Host decides whether that goes to ClickHouse, stdout, or /dev/null
//   • one locale resolver — modules emit key→string bundles, ILocalizer picks
//     the right one by culture at render time
//
// Why this sits in the Host and not individual modules:
//   economics/analytics/locales are user-visible cross-cutting concerns. If
//   each module owned its own balance store, switching games would feel like
//   switching casinos. One ledger, many games.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;

namespace BotFramework.Host;

public interface IEconomicsService
{
    /// Creates the user row if it doesn't exist yet, seeded with the starting
    /// balance from BotFrameworkOptions.StartingCoins. Always updates the
    /// display name on existing rows so Telegram handle changes propagate.
    Task EnsureUserAsync(long userId, string displayName, CancellationToken ct);

    Task<int> GetBalanceAsync(long userId, CancellationToken ct);

    /// Returns false without mutating state if the user's balance would go
    /// negative. Throws if the user doesn't exist — callers ensure first.
    Task<bool> TryDebitAsync(long userId, int amount, string reason, CancellationToken ct);

    /// Convenience: TryDebit + throw InsufficientFundsException on false.
    Task DebitAsync(long userId, int amount, string reason, CancellationToken ct);

    Task CreditAsync(long userId, int amount, string reason, CancellationToken ct);

    /// Admin-only: add or subtract any amount, bypassing the non-negative guard.
    Task AdjustUncheckedAsync(long userId, int delta, CancellationToken ct);
}

/// Modules call Track() with their moduleId + event name + tags. Host decides
/// where it ends up (ClickHouse batch, log line, Prometheus counter). Module
/// never imports a ClickHouse client directly.
public interface IAnalyticsService
{
    void Track(string moduleId, string eventName, IReadOnlyDictionary<string, object?> tags);
}

/// Resolves a localized string. The key is scoped by moduleId automatically
/// (ModuleLoader prefixes bundle keys during merge) so two modules can reuse
/// short keys like "display_name" without collision.
public interface ILocalizer
{
    string Get(string moduleId, string key, string cultureCode = "ru");
    string GetPlural(string moduleId, string key, int count, string cultureCode = "ru");
}

/// Renders the current active game board / turn state into a Telegram message.
/// Each module ships its own IRenderer<TAggregate> so presentation stays with
/// the domain it knows. Host calls Render() uniformly from handlers.
public interface IRenderer<in TAggregate> where TAggregate : IAggregateRoot
{
    RenderedMessage Render(TAggregate aggregate, long viewerUserId, string cultureCode);
}

/// Renders an aggregate into a binary media payload (image/GIF/animation)
/// with caption + optional inline keyboard. Horse races use this to produce
/// an MP4/GIF of the result; Poker admin might snapshot a board as PNG.
/// Modules that only need text-and-buttons implement IRenderer<T> instead;
/// this is the "heavier" variant when a simple text message won't do.
public interface IMediaRenderer<in TAggregate> where TAggregate : IAggregateRoot
{
    RenderedMedia Render(TAggregate aggregate, long viewerUserId, string cultureCode);
}

public sealed record RenderedMessage(string Text, IReadOnlyList<InlineButton> Buttons);

public sealed record RenderedMedia(
    RenderedMediaKind Kind,
    byte[] Content,
    string FileName,
    string? Caption,
    IReadOnlyList<InlineButton> Buttons);

public enum RenderedMediaKind { Photo, Animation, Video, Document }

public sealed record InlineButton(string Label, string CallbackData);
