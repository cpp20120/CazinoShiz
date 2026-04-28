using BotFramework.Host;
using BotFramework.Host.Composition;
using BotFramework.Host.Services;
using Dapper;
using Games.Horse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Host.Pages.Admin;

public sealed partial class HorseModel(
    IHorseService horse,
    IHorseRaceNotifier notifier,
    INpgsqlConnectionFactory connections,
    HorseGifCache gifCache,
    IOptions<HorseOptions> options,
    IAdminAuditLog audit) : PageModel
{
    private readonly HorseOptions _opts = options.Value;

    public int BetsToday { get; private set; }
    public IReadOnlyDictionary<int, double> Koefs { get; private set; } = new Dictionary<int, double>();
    public IReadOnlyList<PastRace> Past { get; private set; } = [];
    public string TodayRaceDate { get; private set; } = "";
    public int MinBets => _opts.MinBetsToRun;
    public int HorseCount => _opts.HorseCount;
    public IReadOnlyList<long> ConfiguredAdmins => _opts.Admins;
    public IReadOnlyList<string> DatesWithGif { get; private set; } = [];

    public string? Flash { get; set; }
    public bool FlashError { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
    {
        TodayRaceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var actor = HttpContext.Session.GetAdminSession();
        if (actor?.Role != AdminRole.SuperAdmin)
            return StatusCode(403);

        var callerId = actor.UserId;

        var outcome = await horse.RunRaceAsync(callerId, HorseRunKind.Global, 0, ct);
        if (outcome.Error != HorseError.None)
        {
            TempData["FlashError"] = $"Race rejected: {outcome.Error}";
            return RedirectToPage();
        }

        gifCache.Put(TodayRaceDate, outcome.GifBytes);

        await notifier.SendResultGifsAsync(outcome, TodayRaceDate, ct);
        notifier.ScheduleWinnerAnnouncements(outcome);
        var flash = $"Race done. Winner: horse {outcome.Winner + 1}. " +
            $"Payouts: {outcome.Transactions.Count}. " +
            $"Chats: {outcome.BetScopeIds.Count}.";
        TempData["Flash"] = flash;

        await audit.LogAsync(actor.UserId, actor.Name, "horse.run_race",
            new { winner = outcome.Winner + 1, payouts = outcome.Transactions.Count, raceDate = TodayRaceDate }, ct);

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        TodayRaceDate = HorseTimeHelper.GetRaceDate(_opts.TimezoneOffsetHours);
        var info = await horse.GetTodayInfoAsync(balanceScopeIdOnly: null, ct);
        BetsToday = info.BetsCount;
        Koefs = info.Koefs;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PastRace>(new CommandDefinition("""
            SELECT race_date AS RaceDate, balance_scope_id AS BalanceScopeId, winner AS Winner
            FROM horse_results
            ORDER BY race_date DESC, balance_scope_id ASC
            LIMIT 60
            """, cancellationToken: ct));
        Past = rows.ToList();
        DatesWithGif = gifCache.Dates.ToList();

        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
        if (FlashError) Flash = TempData["FlashError"] as string;
    }
}

public sealed record PastRace(string RaceDate, long BalanceScopeId, int Winner);
