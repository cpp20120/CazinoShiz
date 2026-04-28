using System.Text.Json;
using System.Text.Json.Nodes;
using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using BotFramework.Host.Services.RuntimeTuning;
using Dapper;
using Games.Basketball;
using Games.Bowling;
using Games.Darts;
using Games.Dice;
using Games.DiceCube;
using Games.Football;
using Games.Horse;
using Games.Transfer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class SettingsModel(
    IConfiguration configuration,
    INpgsqlConnectionFactory connections,
    IRuntimeTuningAccessor tuning,
    IAdminAuditLog audit,
    ILogger<SettingsModel> logger) : PageModel
{
    [BindProperty]
    public string PatchJson { get; set; } = "{}";
    public string EffectivePreviewJson { get; set; } = "";
    public string? Error { get; set; }
    public string? Flash { get; set; }
    public bool CanEdit { get; private set; }
    [BindProperty]
    public StickerGameSettingsInput StickerGames { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null)
            return RedirectToPage("/Admin/Login");

        CanEdit = actor.Role == AdminRole.SuperAdmin;
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT payload::text FROM runtime_tuning WHERE id = 1",
            cancellationToken: ct));
        PatchJson = string.IsNullOrWhiteSpace(row) ? "{}" : FormatJson(row);
        EffectivePreviewJson = FormatJson(BuildEffectiveExport());
        LoadStickerGameSettings();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null)
            return RedirectToPage("/Admin/Login");
        if (actor.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        JsonObject? parsed;
        try
        {
            parsed = JsonNode.Parse(PatchJson) as JsonObject;
        }
        catch (JsonException ex)
        {
            Error = ex.Message;
            CanEdit = true;
            EffectivePreviewJson = FormatJson(BuildEffectiveExport());
            LoadStickerGameSettings();
            return Page();
        }

        if (parsed is null)
        {
            Error = "Payload must be a JSON object.";
            CanEdit = true;
            EffectivePreviewJson = FormatJson(BuildEffectiveExport());
            LoadStickerGameSettings();
            return Page();
        }

        var sanitized = RuntimeTuningPayloadSanitizer.Sanitize(parsed);
        var err = ValidateMerged(configuration, sanitized);
        if (err is not null)
        {
            Error = err;
            CanEdit = true;
            EffectivePreviewJson = FormatJson(BuildEffectiveExport());
            LoadStickerGameSettings();
            return Page();
        }

        var compact = sanitized.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE runtime_tuning
            SET payload = @payload::jsonb, updated_at = now()
            WHERE id = 1
            """,
            new { payload = compact },
            cancellationToken: ct));

        await tuning.ReloadFromDatabaseAsync(ct);
        await audit.LogAsync(actor.UserId, actor.Name, "runtime_tuning.save",
            new { bytes = compact.Length }, ct);
        Flash = "Saved. Live settings reloaded.";
        PatchJson = FormatJson(compact);
        EffectivePreviewJson = FormatJson(BuildEffectiveExport());
        LoadStickerGameSettings();
        CanEdit = true;
        logger.LogInformation("runtime_tuning updated by admin {UserId}", actor.UserId);
        return Page();
    }

    public async Task<IActionResult> OnPostStickerGamesAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor is null)
            return RedirectToPage("/Admin/Login");
        if (actor.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        if (StickerGames.All.Any(g => g.DailyLimit < 0))
        {
            Error = "Daily limits must be 0 or greater.";
            return await ReloadPageForErrorAsync(ct);
        }

        if (StickerGames.All.Any(g => g.DropChance < 0 || g.DropChance > 1))
        {
            Error = "Drop chances must be between 0 and 1. Example: 0.02 = 2%.";
            return await ReloadPageForErrorAsync(ct);
        }

        var patch = await LoadPatchObjectAsync(ct);
        patch["Bot"] ??= new JsonObject();
        patch["Games"] ??= new JsonObject();
        var bot = (JsonObject)patch["Bot"]!;
        var games = (JsonObject)patch["Games"]!;

        bot["TelegramDiceDailyLimit"] ??= new JsonObject();
        var daily = (JsonObject)bot["TelegramDiceDailyLimit"]!;
        daily["MaxRollsPerUserPerDayByGame"] = new JsonObject
        {
            ["dice"] = StickerGames.DiceDailyLimit,
            ["dicecube"] = StickerGames.DiceCubeDailyLimit,
            ["darts"] = StickerGames.DartsDailyLimit,
            ["football"] = StickerGames.FootballDailyLimit,
            ["basketball"] = StickerGames.BasketballDailyLimit,
            ["bowling"] = StickerGames.BowlingDailyLimit,
        };

        SetDropChance(games, "dice", StickerGames.DiceDropChance);
        SetDropChance(games, "dicecube", StickerGames.DiceCubeDropChance);
        SetDropChance(games, "darts", StickerGames.DartsDropChance);
        SetDropChance(games, "football", StickerGames.FootballDropChance);
        SetDropChance(games, "basketball", StickerGames.BasketballDropChance);
        SetDropChance(games, "bowling", StickerGames.BowlingDropChance);

        var sanitized = RuntimeTuningPayloadSanitizer.Sanitize(patch);
        var err = ValidateMerged(configuration, sanitized);
        if (err is not null)
        {
            Error = err;
            return await ReloadPageForErrorAsync(ct);
        }

        var compact = sanitized.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        await SavePatchAsync(compact, ct);
        await tuning.ReloadFromDatabaseAsync(ct);
        await audit.LogAsync(actor.UserId, actor.Name, "runtime_tuning.sticker_games.save",
            new { games = StickerGames.All.Select(g => g.GameId).ToArray() }, ct);

        Flash = "Sticker game drop chances and daily limits saved.";
        PatchJson = FormatJson(compact);
        EffectivePreviewJson = FormatJson(BuildEffectiveExport());
        LoadStickerGameSettings();
        CanEdit = true;
        return Page();
    }

    private JsonObject BuildEffectiveExport()
    {
        var bot = new JsonObject
        {
            ["DailyBonus"] = JsonSerializer.SerializeToNode(tuning.DailyBonus),
            ["TelegramDiceDailyLimit"] = JsonSerializer.SerializeToNode(tuning.TelegramDiceDailyLimit),
        };
        var games = new JsonObject
        {
            ["dice"] = JsonSerializer.SerializeToNode(tuning.GetSection<DiceOptions>(DiceOptions.SectionName)),
            ["dicecube"] = JsonSerializer.SerializeToNode(tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName)),
            ["darts"] = JsonSerializer.SerializeToNode(tuning.GetSection<DartsOptions>(DartsOptions.SectionName)),
            ["football"] = JsonSerializer.SerializeToNode(tuning.GetSection<FootballOptions>(FootballOptions.SectionName)),
            ["basketball"] = JsonSerializer.SerializeToNode(tuning.GetSection<BasketballOptions>(BasketballOptions.SectionName)),
            ["bowling"] = JsonSerializer.SerializeToNode(tuning.GetSection<BowlingOptions>(BowlingOptions.SectionName)),
            ["horse"] = JsonSerializer.SerializeToNode(tuning.GetSection<HorseOptions>(HorseOptions.SectionName)),
            ["transfer"] = JsonSerializer.SerializeToNode(tuning.GetSection<TransferOptions>(TransferOptions.SectionName)),
        };
        return new JsonObject { ["Bot"] = bot, ["Games"] = games };
    }

    private async Task<IActionResult> ReloadPageForErrorAsync(CancellationToken ct)
    {
        CanEdit = true;
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT payload::text FROM runtime_tuning WHERE id = 1",
            cancellationToken: ct));
        PatchJson = string.IsNullOrWhiteSpace(row) ? "{}" : FormatJson(row);
        EffectivePreviewJson = FormatJson(BuildEffectiveExport());
        return Page();
    }

    private async Task<JsonObject> LoadPatchObjectAsync(CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT payload::text FROM runtime_tuning WHERE id = 1",
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(row))
            return new JsonObject();

        return JsonNode.Parse(row) as JsonObject ?? new JsonObject();
    }

    private async Task SavePatchAsync(string compactJson, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE runtime_tuning
            SET payload = @payload::jsonb, updated_at = now()
            WHERE id = 1
            """,
            new { payload = compactJson },
            cancellationToken: ct));
    }

    private static void SetDropChance(JsonObject games, string gameId, double dropChance)
    {
        games[gameId] ??= new JsonObject();
        var game = (JsonObject)games[gameId]!;
        game["RedeemDropChance"] = dropChance;
    }

    private void LoadStickerGameSettings()
    {
        var daily = tuning.TelegramDiceDailyLimit;
        StickerGames = new StickerGameSettingsInput
        {
            DiceDropChance = tuning.GetSection<DiceOptions>(DiceOptions.SectionName).RedeemDropChance,
            DiceDailyLimit = daily.GetMaxRollsPerUserPerDay("dice"),
            DiceCubeDropChance = tuning.GetSection<DiceCubeOptions>(DiceCubeOptions.SectionName).RedeemDropChance,
            DiceCubeDailyLimit = daily.GetMaxRollsPerUserPerDay("dicecube"),
            DartsDropChance = tuning.GetSection<DartsOptions>(DartsOptions.SectionName).RedeemDropChance,
            DartsDailyLimit = daily.GetMaxRollsPerUserPerDay("darts"),
            FootballDropChance = tuning.GetSection<FootballOptions>(FootballOptions.SectionName).RedeemDropChance,
            FootballDailyLimit = daily.GetMaxRollsPerUserPerDay("football"),
            BasketballDropChance = tuning.GetSection<BasketballOptions>(BasketballOptions.SectionName).RedeemDropChance,
            BasketballDailyLimit = daily.GetMaxRollsPerUserPerDay("basketball"),
            BowlingDropChance = tuning.GetSection<BowlingOptions>(BowlingOptions.SectionName).RedeemDropChance,
            BowlingDailyLimit = daily.GetMaxRollsPerUserPerDay("bowling"),
        };
    }

    private static string? ValidateMerged(IConfiguration configuration, JsonObject sanitized)
    {
        try
        {
            void TryMerge<T>(JsonObject g, string key, string path) where T : class, new()
            {
                if (g.TryGetPropertyValue(key, out var node) && node is not null)
                    RuntimeTuningMerge.MergeSection<T>(configuration, path, node);
            }

            if (sanitized["Bot"] is JsonObject bot)
            {
                if (bot["DailyBonus"] is JsonNode n)
                    RuntimeTuningMerge.MergeSection<DailyBonusOptions>(configuration, DailyBonusOptions.SectionName, n);
                if (bot["TelegramDiceDailyLimit"] is JsonNode n2)
                    RuntimeTuningMerge.MergeSection<TelegramDiceDailyLimitOptions>(
                        configuration, TelegramDiceDailyLimitOptions.SectionName, n2);
            }

            if (sanitized["Games"] is JsonObject games)
            {
                TryMerge<DiceOptions>(games, "dice", DiceOptions.SectionName);
                TryMerge<DiceCubeOptions>(games, "dicecube", DiceCubeOptions.SectionName);
                TryMerge<DartsOptions>(games, "darts", DartsOptions.SectionName);
                TryMerge<FootballOptions>(games, "football", FootballOptions.SectionName);
                TryMerge<BasketballOptions>(games, "basketball", BasketballOptions.SectionName);
                TryMerge<BowlingOptions>(games, "bowling", BowlingOptions.SectionName);
                TryMerge<HorseOptions>(games, "horse", HorseOptions.SectionName);
                TryMerge<TransferOptions>(games, "transfer", TransferOptions.SectionName);
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        return null;
    }

    private static string FormatJson(string json)
    {
        try
        {
            var n = JsonNode.Parse(json);
            return n?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? json;
        }
        catch
        {
            return json;
        }
    }

    private string FormatJson(JsonObject obj) =>
        obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public sealed class StickerGameSettingsInput
    {
        public double DiceDropChance { get; set; }
        public int DiceDailyLimit { get; set; }
        public double DiceCubeDropChance { get; set; }
        public int DiceCubeDailyLimit { get; set; }
        public double DartsDropChance { get; set; }
        public int DartsDailyLimit { get; set; }
        public double FootballDropChance { get; set; }
        public int FootballDailyLimit { get; set; }
        public double BasketballDropChance { get; set; }
        public int BasketballDailyLimit { get; set; }
        public double BowlingDropChance { get; set; }
        public int BowlingDailyLimit { get; set; }

        public IEnumerable<(string GameId, double DropChance, int DailyLimit)> All
        {
            get
            {
                yield return ("dice", DiceDropChance, DiceDailyLimit);
                yield return ("dicecube", DiceCubeDropChance, DiceCubeDailyLimit);
                yield return ("darts", DartsDropChance, DartsDailyLimit);
                yield return ("football", FootballDropChance, FootballDailyLimit);
                yield return ("basketball", BasketballDropChance, BasketballDailyLimit);
                yield return ("bowling", BowlingDropChance, BowlingDailyLimit);
            }
        }
    }
}
