// ─────────────────────────────────────────────────────────────────────────────
// BotFrameworkOptions — framework-level options every distribution binds.
// Per-module options are bound separately by each IModule via BindOptions.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Host;

public sealed class BotFrameworkOptions
{
    public const string SectionName = "Bot";

    /// Telegram bot token. Required.
    public string Token { get; set; } = "";

    /// True when running behind a Telegram webhook (updates arrive via HTTP POST
    /// at /{Token}). False for dev polling.
    public bool IsProduction { get; set; }

    /// Kestrel port for webhook mode. Ignored in polling mode.
    public int WebhookPort { get; set; } = 3000;

    /// Secret for gating /admin/* pages. Leave empty to disable the admin UI
    /// entirely (framework returns 503 when admin routes are hit).
    public string? AdminWebToken { get; set; }

    /// Default culture for ILocalizer when an update has no culture hint.
    public string DefaultCulture { get; set; } = "ru";

    /// Coins newly seeded users start with. Applied by EconomicsService when
    /// EnsureUserAsync inserts a user row for the first time.
    public int StartingCoins { get; set; } = 100;

    /// Channel @username (with or without leading "@") used as the public
    /// broadcast target — e.g. the admin horse panel posts the race GIF here.
    /// Empty string disables broadcasting.
    public string TrustedChannel { get; set; } = "";
}
