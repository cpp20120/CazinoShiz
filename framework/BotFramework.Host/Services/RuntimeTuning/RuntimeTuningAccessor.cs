using System.Text.Json;
using System.Text.Json.Nodes;
using BotFramework.Host;
using BotFramework.Host.Composition;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace BotFramework.Host.Services.RuntimeTuning;

public sealed class RuntimeTuningAccessor : IRuntimeTuningAccessor, IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly INpgsqlConnectionFactory _connections;
    private readonly ILogger<RuntimeTuningAccessor> _logger;
    private JsonObject? _patchRoot;
    private readonly Lock _reloadGate = new();

    public RuntimeTuningAccessor(
        IConfiguration configuration,
        INpgsqlConnectionFactory connections,
        ILogger<RuntimeTuningAccessor> logger)
    {
        _configuration = configuration;
        _connections = connections;
        _logger = logger;
    }

    public DailyBonusOptions DailyBonus =>
        RuntimeTuningMerge.MergeSection<DailyBonusOptions>(
            _configuration, DailyBonusOptions.SectionName, Navigate(DailyBonusOptions.SectionName));

    public TelegramDiceDailyLimitOptions TelegramDiceDailyLimit =>
        RuntimeTuningMerge.MergeSection<TelegramDiceDailyLimitOptions>(
            _configuration, TelegramDiceDailyLimitOptions.SectionName, Navigate(TelegramDiceDailyLimitOptions.SectionName));

    public T GetSection<T>(string sectionPath) where T : class, new() =>
        RuntimeTuningMerge.MergeSection<T>(_configuration, sectionPath, Navigate(sectionPath));

    public async Task ReloadFromDatabaseAsync(CancellationToken ct)
    {
        JsonObject? nextPatch = null;
        await using var conn = await _connections.OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT payload::text FROM runtime_tuning WHERE id = 1",
            cancellationToken: ct)).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(row))
        {
            try
            {
                nextPatch = JsonNode.Parse(row) as JsonObject;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "runtime_tuning.payload is invalid JSON; ignoring DB overrides");
            }
        }

        lock (_reloadGate)
            _patchRoot = nextPatch;
    }

    private JsonNode? Navigate(string sectionPath)
    {
        JsonObject? root;
        lock (_reloadGate)
            root = _patchRoot;

        if (root is null) return null;

        var parts = sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        JsonNode? n = root;
        foreach (var p in parts)
        {
            if (n is not JsonObject o) return null;
            if (!o.TryGetPropertyValue(p, out var child)) return null;
            n = child;
        }

        return n;
    }

    public async Task StartAsync(CancellationToken cancellationToken) =>
        await ReloadFromDatabaseAsync(cancellationToken).ConfigureAwait(false);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
