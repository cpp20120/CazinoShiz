// ─────────────────────────────────────────────────────────────────────────────
// IModule — the contract every game implements.
//
// A module is a self-contained feature: its DI registrations, entity
// configurations, locales, route handlers, and config options all live here.
// Adding a game to a Host = reference the module's assembly + register it once.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Long-running worker registered by a module. Host starts it on app start
/// and cancels the token on shutdown. Exceptions do NOT bring the Host down —
/// the runner logs and restarts with backoff.
public interface IBackgroundJob
{
    string Name { get; }
    Task RunAsync(CancellationToken stoppingToken);
}
