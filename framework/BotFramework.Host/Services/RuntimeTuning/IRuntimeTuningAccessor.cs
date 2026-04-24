using BotFramework.Host.Composition;

namespace BotFramework.Host.Services;

/// <summary>
/// Effective configuration for tunable sections: merges <c>appsettings</c> / env with <c>runtime_tuning.payload</c> (DB).
/// Game modules call <see cref="GetSection{T}"/> with their <c>Games:…</c> path; framework types use the typed properties.
/// </summary>
public interface IRuntimeTuningAccessor
{
    DailyBonusOptions DailyBonus { get; }
    TelegramDiceDailyLimitOptions TelegramDiceDailyLimit { get; }

    /// <summary>Merges file/env for <paramref name="sectionPath"/> (e.g. <c>Games:dice</c>) with the DB patch.</summary>
    T GetSection<T>(string sectionPath) where T : class, new();

    Task ReloadFromDatabaseAsync(CancellationToken ct);
}
