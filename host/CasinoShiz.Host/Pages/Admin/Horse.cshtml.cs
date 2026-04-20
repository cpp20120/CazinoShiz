using BotFramework.Host;
using BotFramework.Host.Composition;
using Dapper;
using Games.Horse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace CasinoShiz.Host.Pages.Admin;

public sealed partial class HorseModel(
    IHorseService horse,
    INpgsqlConnectionFactory connections,
    HorseGifCache gifCache,
    IOptions<HorseOptions> options,
    IOptions<BotFrameworkOptions> botOptions,
    ITelegramBotClient bot,
    ILogger<HorseModel> logger) : PageModel
{
    private readonly HorseOptions _opts = options.Value;
    private readonly BotFrameworkOptions _botOpts = botOptions.Value;

    public int BetsToday { get; private set; }
    public IReadOnlyDictionary<int, double> Koefs { get; private set; } = new Dictionary<int, double>();
    public IReadOnlyList<PastRace> Past { get; private set; } = [];
    public string TodayRaceDate { get; private set; } = HorseTimeHelper.GetRaceDate();
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
        var callerId = _opts.Admins.FirstOrDefault();
        if (callerId == 0)
        {
            TempData["FlashError"] = "Games:horse:Admins is empty — add your Telegram id to config.";
            return RedirectToPage();
        }

        var outcome = await horse.RunRaceAsync(callerId, ct);
        if (outcome.Error != HorseError.None)
        {
            TempData["FlashError"] = $"Race rejected: {outcome.Error}";
            return RedirectToPage();
        }

        gifCache.Put(TodayRaceDate, outcome.GifBytes);

        var (channelStatus, dmsOk, dmsFail) = await BroadcastAsync(outcome, ct);
        TempData["Flash"] =
            $"Race done. Winner: horse {outcome.Winner + 1}. " +
            $"Payouts: {outcome.Transactions.Count}. " +
            $"Channel: {channelStatus}. DMs: {dmsOk}/{dmsOk + dmsFail}.";
        return RedirectToPage();
    }

    private async Task<(string ChannelStatus, int DmsOk, int DmsFail)> BroadcastAsync(
        RaceOutcome outcome, CancellationToken ct)
    {
        var caption = $"🐎 Race {TodayRaceDate} · winner horse #{outcome.Winner + 1}";

        string? reusableFileId = null;
        var channelStatus = "skipped";
        var trusted = _botOpts.TrustedChannel?.Trim();
        if (!string.IsNullOrEmpty(trusted))
        {
            var target = trusted.StartsWith('@') ? trusted : "@" + trusted;
            try
            {
                await using var stream = new MemoryStream(outcome.GifBytes);
                var sent = await bot.SendAnimation(
                    target, InputFile.FromStream(stream, "horses.gif"),
                    caption: caption, cancellationToken: ct);
                reusableFileId = sent.Animation?.FileId;
                channelStatus = "ok";
            }
            catch (Exception ex)
            {
                LogBroadcastFailed(target, ex);
                channelStatus = "failed";
            }
        }

        int dmsOk = 0, dmsFail = 0;
        foreach (var p in outcome.Participants)
        {
            var personal = p.Payout > 0
                ? $"🏆 Race {TodayRaceDate}: horse #{outcome.Winner + 1} won. You bet {p.TotalBet}, payout {p.Payout}."
                : $"🐎 Race {TodayRaceDate}: horse #{outcome.Winner + 1} won. You bet {p.TotalBet}, no payout.";
            try
            {
                InputFile gif = reusableFileId is not null
                    ? InputFile.FromFileId(reusableFileId)
                    : InputFile.FromStream(new MemoryStream(outcome.GifBytes), "horses.gif");

                var sent = await bot.SendAnimation(
                    p.UserId, gif, caption: personal, cancellationToken: ct);
                reusableFileId ??= sent.Animation?.FileId;
                dmsOk++;
            }
            catch (Exception ex)
            {
                LogDmFailed(p.UserId, ex);
                dmsFail++;
            }
        }
        return (channelStatus, dmsOk, dmsFail);
    }

    [LoggerMessage(EventId = 4701, Level = LogLevel.Warning,
        Message = "admin.horse.broadcast.failed target={Target}")]
    private partial void LogBroadcastFailed(string target, Exception exception);

    [LoggerMessage(EventId = 4702, Level = LogLevel.Information,
        Message = "admin.horse.dm.failed user={UserId}")]
    private partial void LogDmFailed(long userId, Exception exception);

    private async Task LoadAsync(CancellationToken ct)
    {
        var info = await horse.GetTodayInfoAsync(ct);
        BetsToday = info.BetsCount;
        Koefs = info.Koefs;

        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PastRace>(new CommandDefinition("""
            SELECT race_date AS RaceDate, winner AS Winner
            FROM horse_results
            ORDER BY race_date DESC
            LIMIT 30
            """, cancellationToken: ct));
        Past = rows.ToList();
        DatesWithGif = gifCache.Dates.ToList();

        Flash = TempData["Flash"] as string;
        FlashError = TempData["FlashError"] is not null;
        if (FlashError) Flash = TempData["FlashError"] as string;
    }
}

public sealed record PastRace(string RaceDate, int Winner);
