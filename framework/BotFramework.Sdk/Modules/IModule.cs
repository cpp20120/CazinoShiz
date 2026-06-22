namespace BotFramework.Sdk;

public interface IModule
{
    /// Stable machine identifier: "poker", "sh", "blackjack". Used as the config
    /// section key (Games:<Id>), the event-type prefix, and the admin URL segment.
    string Id { get; }

    /// Human-readable name for menus and admin UI. Localized via GetLocales.
    string DisplayName { get; }

    /// Semver for the module. Surfaced to admin UI; also gates migrations.
    string Version { get; }

    /// Called once at Host startup. Bind options, register services, register
    /// handlers, register repositories. Modules MUST NOT touch services other
    /// modules own - only the Host-provided abstractions.
    void ConfigureServices(IModuleServiceCollection services);

    /// Per-module schema. Host applies migrations against the module's own
    /// tracking table (__module_migrations) before any service starts.
    IModuleMigrations? GetMigrations() => null;

    /// Returns the module's localization resources.
    IReadOnlyList<LocaleBundle> GetLocales();

    /// Optional. Returns Telegram bot-menu commands this module contributes.
    IReadOnlyList<BotCommand> GetBotCommands() => [];

    /// Optional. Called when the Host is shutting down.
    Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
}
