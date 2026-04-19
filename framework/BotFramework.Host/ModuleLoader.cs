// ─────────────────────────────────────────────────────────────────────────────
// ModuleLoader — Host-side bootstrap. Discovers IModule implementations across
// referenced assemblies and wires them into the DI container, locale store,
// bot command menu, and update router in one pass.
//
// Flow at Host startup:
//   1. Discover — reflect over loaded assemblies for IModule types.
//   2. Instantiate each module (parameterless ctor, same pattern as handlers).
//   3. ConfigureServices on each — modules bind their options + register their
//      services + aggregates + handlers + projections + admin pages + jobs.
//   4. Collect GetMigrations() from each — ModuleMigrationRunner applies them
//      against __module_migrations tracking table before the app starts.
//   5. Locales merged into a single ResourceManager keyed by (culture, moduleId).
//   6. Bot commands aggregated and SetMyCommands called once.
//   7. UpdateRouter scans all module assemblies for [Command]/[CallbackPrefix]
//      attributed handlers (unchanged from current CasinoShiz behavior —
//      just over a wider assembly set).
//
// Schema is NOT Host-owned anymore: each module ships its own migration chain,
// Host just orchestrates. The only Host-owned tables are module_events,
// module_snapshots, and __module_migrations.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.Extensions.Configuration;

namespace BotFramework.Host;

public sealed class ModuleLoader
{
    /// Convenience overload for the composition root. Creates the adapter +
    /// registrations bag, registers both as singletons on the real service
    /// collection, invokes ConfigureServices on each module, and returns the
    /// aggregated LoadedModules view.
    public static LoadedModules LoadAll(
        IEnumerable<IModule> modules,
        IServiceCollection services,
        IConfiguration configuration)
    {
        var registrations = new ModuleRegistrations();
        services.AddSingleton(registrations);

        var adapter = new ModuleServiceCollectionAdapter(services, configuration, registrations);
        var loaded = LoadAll(modules, adapter);

        services.AddSingleton(loaded);
        return loaded;
    }

    public static LoadedModules LoadAll(
        IEnumerable<IModule> modules,
        IModuleServiceCollection services)
    {
        var loaded = new List<IModule>();
        var localesByCulture = new Dictionary<string, Dictionary<string, string>>();
        var botCommands = new List<BotCommand>();
        var migrations = new List<IModuleMigrations>();

        foreach (var module in modules)
        {
            module.ConfigureServices(services);

            if (module.GetMigrations() is { } m)
                migrations.Add(m);

            foreach (var bundle in module.GetLocales())
            {
                if (!localesByCulture.TryGetValue(bundle.CultureCode, out var dict))
                    localesByCulture[bundle.CultureCode] = dict = new();

                foreach (var (key, value) in bundle.Strings)
                    dict[$"{module.Id}.{key}"] = value;
            }

            botCommands.AddRange(module.GetBotCommands());
            loaded.Add(module);
        }

        return new LoadedModules(loaded, localesByCulture, botCommands, migrations);
    }
}

public sealed record LoadedModules(
    IReadOnlyList<IModule> Modules,
    IReadOnlyDictionary<string, Dictionary<string, string>> LocalesByCulture,
    IReadOnlyList<BotCommand> BotCommands,
    IReadOnlyList<IModuleMigrations> Migrations);
