// ─────────────────────────────────────────────────────────────────────────────
// Localizer — resolves module-scoped localized strings from LoadedModules.
//
// Modules contribute LocaleBundles during ConfigureServices(); the builder
// merges them keyed by (cultureCode, "<moduleId>.<key>"). This service reads
// that merged map and falls back along a predictable chain:
//
//   1. requested culture, scoped key       ("en" + "poker.welcome")
//   2. default culture, scoped key         ("ru" + "poker.welcome")
//   3. literal "<moduleId>.<key>"          so a missing string surfaces in
//                                          logs/UI as itself, not as a crash
//
// Plural forms are Russian-only today because every shipped module uses
// Russian; when a module needs another culture's plural rules the module
// passes its own plural form array into GetPlural's "forms" parameter. The
// framework-level Russian fallback mirrors the live bot's RussianPlural
// helper so no call site changes shape.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host.Composition;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;

namespace BotFramework.Host;

public sealed class Localizer(
    LoadedModules loadedModules,
    IOptions<BotFrameworkOptions> options) : ILocalizer
{
    private readonly string _defaultCulture = options.Value.DefaultCulture;

    public string Get(string moduleId, string key, string cultureCode = "ru")
    {
        var scopedKey = $"{moduleId}.{key}";
        if (TryLookup(cultureCode, scopedKey, out var value)) return value;
        if (cultureCode != _defaultCulture && TryLookup(_defaultCulture, scopedKey, out value)) return value;
        return scopedKey;
    }

    public string GetPlural(string moduleId, string key, int count, string cultureCode = "ru")
    {
        // Convention: the module ships three forms under keys
        //   "<key>.one"    — 1 апельсин
        //   "<key>.few"    — 2 апельсина
        //   "<key>.many"   — 5 апельсинов
        // Mirrors how the live CasinoShiz Locales use RussianPlural.
        var idx = PluralIndex(cultureCode, count);
        var suffix = idx switch { 0 => ".one", 1 => ".few", _ => ".many" };
        return Get(moduleId, key + suffix, cultureCode);
    }

    private bool TryLookup(string cultureCode, string scopedKey, out string value)
    {
        if (loadedModules.LocalesByCulture.TryGetValue(cultureCode, out var dict)
            && dict.TryGetValue(scopedKey, out var found))
        {
            value = found;
            return true;
        }
        value = "";
        return false;
    }

    private static int PluralIndex(string cultureCode, int count)
    {
        // Russian: one (1, 21, 31…), few (2–4, 22–24…), many (0, 5–20…).
        // Other cultures fall back to English-style two-form: one (1) / many (other).
        if (cultureCode.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
        {
            var abs = Math.Abs(count);
            var mod100 = abs % 100;
            if (mod100 is >= 5 and <= 20) return 2;
            var mod10 = abs % 10;
            return mod10 switch { 1 => 0, >= 2 and <= 4 => 1, _ => 2 };
        }

        return count == 1 ? 0 : 2;
    }
}
