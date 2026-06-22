// ─────────────────────────────────────────────────────────────────────────────
// IModule — the contract every game implements.
//
// A module is a self-contained feature: its DI registrations, entity
// configurations, locales, route handlers, and config options all live here.
// Adding a game to a Host = reference the module's assembly + register it once.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
public sealed record BotCommand(string Command, string DescriptionKey);
