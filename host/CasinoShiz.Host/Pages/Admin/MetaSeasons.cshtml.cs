using BotFramework.Host;
using BotFramework.Host.Services;
using BotFramework.Sdk;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CasinoShiz.Host.Pages.Admin;

public sealed class MetaSeasonsModel(
    INpgsqlConnectionFactory connections,
    IEconomicsService economics,
    IAdminAuditLog audit) : PageModel
{
    public IReadOnlyList<MetaSeasonAdminRow> Seasons { get; private set; } = [];
    public AdminSession? Actor { get; private set; }
    public bool CanEdit { get; private set; }

    public string? Flash { get; set; }
    public bool FlashError { get; set; }

    [BindProperty] public string Name { get; set; } = "Season";
    [BindProperty] public int DurationDays { get; set; } = 30;
    [BindProperty] public string ConfigJson { get; set; } = DefaultConfigJson;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Actor = HttpContext.Session.GetAdminSession();
        if (Actor is null) return RedirectToPage("/Admin/Login");

        CanEdit = Actor.Role == AdminRole.SuperAdmin;
        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
        ConfigJson = DefaultConfigJson;
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        Name = string.IsNullOrWhiteSpace(Name) ? "Season" : Name.Trim();
        DurationDays = Math.Clamp(DurationDays, 1, 365);
        if (!IsJsonObject(ConfigJson))
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = "Config must be a valid JSON object.";
            return RedirectToPage();
        }

        const string sql = """
            INSERT INTO meta_seasons (name, starts_at, ends_at, status, config)
            VALUES (@name, date_trunc('day', now()), date_trunc('day', now()) + (@durationDays || ' days')::interval, 'planned', CAST(@configJson AS jsonb))
            RETURNING id
            """;

        await using var conn = await connections.OpenAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { name = Name, durationDays = DurationDays, configJson = ConfigJson }, cancellationToken: ct));
        await audit.LogAsync(actor.UserId, actor.Name, "meta_season.create", new { id, Name, DurationDays }, ct);

        TempData["Flash"] = $"Season #{id} created as planned.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostActivateAsync(long seasonId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        await using var conn = await connections.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_seasons SET status = 'finished', updated_at = now() WHERE status = 'active'",
            transaction: tx,
            cancellationToken: ct));

        var changed = await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE meta_seasons SET status = 'active', starts_at = LEAST(starts_at, now()), updated_at = now() WHERE id = @seasonId AND status IN ('planned', 'finished')",
            new { seasonId },
            transaction: tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);

        if (changed > 0)
        {
            await audit.LogAsync(actor.UserId, actor.Name, "meta_season.activate", new { seasonId }, ct);
            TempData["Flash"] = $"Season #{seasonId} activated.";
        }
        else
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Season #{seasonId} was not activated.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostFinishAsync(long seasonId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        const string sql = """
            UPDATE meta_seasons
            SET status = 'finished', ends_at = LEAST(ends_at, now()), updated_at = now()
            WHERE id = @seasonId AND status <> 'finished'
            """;

        await using var conn = await connections.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(new CommandDefinition(sql, new { seasonId }, cancellationToken: ct));
        if (changed > 0)
        {
            await audit.LogAsync(actor.UserId, actor.Name, "meta_season.finish", new { seasonId }, ct);
            TempData["Flash"] = $"Season #{seasonId} finished.";
        }
        else
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Season #{seasonId} was not changed.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayRewardsAsync(long seasonId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        const string seasonSql = "SELECT status FROM meta_seasons WHERE id = @seasonId";
        const string topSql = """
            SELECT row_number() OVER (ORDER BY xp DESC, rating DESC, user_id ASC)::int AS Place,
                   chat_id AS ChatId,
                   user_id AS UserId,
                   display_name AS DisplayName,
                   xp AS Xp,
                   rating AS Rating
            FROM meta_season_players
            WHERE season_id = @seasonId
            ORDER BY xp DESC, rating DESC, user_id ASC
            LIMIT 3
            """;

        await using var conn = await connections.OpenAsync(ct);
        var status = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(seasonSql, new { seasonId }, cancellationToken: ct));
        if (status is null)
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Season #{seasonId} not found.";
            return RedirectToPage();
        }

        var winners = (await conn.QueryAsync<SeasonRewardWinner>(new CommandDefinition(topSql, new { seasonId }, cancellationToken: ct))).ToList();
        var paid = 0;
        foreach (var winner in winners)
        {
            var amount = RewardForPlace(winner.Place);
            if (amount <= 0) continue;

            await economics.EnsureUserAsync(winner.UserId, winner.ChatId, winner.DisplayName, ct);
            await economics.CreditOnceAsync(
                winner.UserId,
                winner.ChatId,
                amount,
                "season.reward",
                $"season:reward:{seasonId}:{winner.Place}:{winner.ChatId}:{winner.UserId}",
                ct);
            paid++;
        }

        await audit.LogAsync(actor.UserId, actor.Name, "meta_season.pay_rewards", new
        {
            seasonId,
            winners = winners.Select(x => new { x.Place, x.ChatId, x.UserId, x.DisplayName, amount = RewardForPlace(x.Place) }).ToArray(),
        }, ct);

        TempData["Flash"] = $"Season #{seasonId} rewards processed for {paid} winners.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPayClanRewardsAsync(long seasonId, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        const string seasonSql = "SELECT status FROM meta_seasons WHERE id = @seasonId";
        const string topSql = """
            SELECT row_number() OVER (ORDER BY sc.xp DESC, sc.rating DESC, sc.clan_id ASC)::int AS Place,
                   sc.chat_id AS ChatId,
                   sc.clan_id AS ClanId,
                   c.name AS ClanName,
                   c.tag AS ClanTag,
                   c.owner_user_id AS OwnerUserId,
                   COALESCE(m.display_name, c.owner_user_id::text) AS OwnerDisplayName,
                   sc.xp AS Xp,
                   sc.rating AS Rating
            FROM meta_season_clans sc
            JOIN meta_clans c ON c.id = sc.clan_id
            LEFT JOIN meta_clan_members m ON m.clan_id = c.id AND m.user_id = c.owner_user_id
            WHERE sc.season_id = @seasonId
            ORDER BY sc.xp DESC, sc.rating DESC, sc.clan_id ASC
            LIMIT 3
            """;

        await using var conn = await connections.OpenAsync(ct);
        var status = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(seasonSql, new { seasonId }, cancellationToken: ct));
        if (status is null)
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Season #{seasonId} not found.";
            return RedirectToPage();
        }

        var winners = (await conn.QueryAsync<ClanRewardWinner>(new CommandDefinition(topSql, new { seasonId }, cancellationToken: ct))).ToList();
        var paid = 0;
        foreach (var winner in winners)
        {
            var amount = ClanRewardForPlace(winner.Place);
            if (amount <= 0) continue;

            await economics.EnsureUserAsync(winner.OwnerUserId, winner.ChatId, winner.OwnerDisplayName, ct);
            await economics.CreditOnceAsync(
                winner.OwnerUserId,
                winner.ChatId,
                amount,
                "season.clan_reward",
                $"season:clan-reward:{seasonId}:{winner.Place}:{winner.ChatId}:{winner.ClanId}:{winner.OwnerUserId}",
                ct);
            paid++;
        }

        await audit.LogAsync(actor.UserId, actor.Name, "meta_season.pay_clan_rewards", new
        {
            seasonId,
            winners = winners.Select(x => new { x.Place, x.ChatId, x.ClanId, x.ClanTag, x.ClanName, x.OwnerUserId, amount = ClanRewardForPlace(x.Place) }).ToArray(),
        }, ct);

        TempData["Flash"] = $"Season #{seasonId} clan rewards processed for {paid} clans.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateConfigAsync(long seasonId, string configJson, CancellationToken ct)
    {
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin) return Forbid();

        if (!IsJsonObject(configJson))
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = "Config must be a valid JSON object.";
            return RedirectToPage();
        }

        const string sql = """
            UPDATE meta_seasons
            SET config = CAST(@configJson AS jsonb), updated_at = now()
            WHERE id = @seasonId
            """;

        await using var conn = await connections.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(new CommandDefinition(sql, new { seasonId, configJson }, cancellationToken: ct));
        if (changed > 0)
        {
            await audit.LogAsync(actor.UserId, actor.Name, "meta_season.config_update", new { seasonId }, ct);
            TempData["Flash"] = $"Season #{seasonId} config updated.";
        }
        else
        {
            TempData["FlashError"] = true;
            TempData["Flash"] = $"Season #{seasonId} was not found.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id,
                   name AS Name,
                   starts_at AS StartsAt,
                   ends_at AS EndsAt,
                   status AS Status,
                   config::text AS ConfigJson,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
            FROM meta_seasons
            ORDER BY CASE status WHEN 'active' THEN 0 WHEN 'planned' THEN 1 ELSE 2 END, starts_at DESC, id DESC
            LIMIT 100
            """;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<MetaSeasonAdminRow>(new CommandDefinition(sql, cancellationToken: ct));
        Seasons = rows.ToList();
    }

    private static int RewardForPlace(int place) => place switch
    {
        1 => 5_000,
        2 => 2_500,
        3 => 1_000,
        _ => 0,
    };

    private static int ClanRewardForPlace(int place) => place switch
    {
        1 => 10_000,
        2 => 5_000,
        3 => 2_500,
        _ => 0,
    };

    private static bool IsJsonObject(string value)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private const string DefaultConfigJson = """
        {
          "xp": { "play": 5, "win": 25, "loss": 2, "stakeMultiplier": 0.01, "maxXpPerGame": 500 },
          "rating": { "enabled": true, "start": 1000, "winDelta": 16, "lossDelta": -12 },
          "quests": { "dailyEnabled": true, "weeklyEnabled": true },
          "achievements": { "enabled": true },
          "clans": { "enabled": true, "maxMembers": 20 },
          "tournaments": { "enabled": true, "maxActivePerChat": 3 },
          "risk": { "enabled": true, "largeWinMultiplierAlert": 20, "suspiciousStreakThreshold": 12 }
        }
        """;
}

public sealed record MetaSeasonAdminRow(
    long Id,
    string Name,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Status,
    string ConfigJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SeasonRewardWinner(
    int Place,
    long ChatId,
    long UserId,
    string DisplayName,
    long Xp,
    int Rating);

public sealed record ClanRewardWinner(
    int Place,
    long ChatId,
    long ClanId,
    string ClanName,
    string ClanTag,
    long OwnerUserId,
    string OwnerDisplayName,
    long Xp,
    int Rating);
