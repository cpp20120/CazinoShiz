using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Games.Horse;

/// <summary>
/// When <see cref="HorseOptions.AutoRunEnabled"/> is true, runs one global race per calendar day
/// (see <see cref="HorseOptions.TimezoneOffsetHours"/>) after the configured local wall time.
/// Settles payouts like <c>/horserun global</c>; it does not post to Telegram (users see <c>/horse result</c>).
/// </summary>
public sealed partial class HorseScheduledRaceJob(
    IServiceProvider services,
    IOptionsMonitor<HorseOptions> optionsMonitor,
    ILogger<HorseScheduledRaceJob> logger) : IBackgroundJob
{
    public string Name => "horse.scheduled_race";

    /// <summary>If set, we already skipped or failed for this <c>race_date</c> (e.g. not enough bets).</summary>
    private string? _skippedOrFailedRaceDate;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = optionsMonitor.CurrentValue;
            try
            {
                if (!opts.AutoRunEnabled)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (opts.Admins.Count == 0)
                {
                    LogAutoRunNoAdmins();
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var tzHours = opts.TimezoneOffsetHours;
                var raceDate = HorseTimeHelper.GetRaceDate(tzHours);
                if (raceDate != _skippedOrFailedRaceDate)
                    _skippedOrFailedRaceDate = null;

                var offset = TimeSpan.FromHours(tzHours);
                var nowLocal = DateTimeOffset.UtcNow.ToOffset(offset);

                using var scope = services.CreateScope();
                var sp = scope.ServiceProvider;
                var horse = sp.GetRequiredService<IHorseService>();
                var results = sp.GetRequiredService<IHorseResultStore>();

                var existing = await results.FindAsync(raceDate, 0, stoppingToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var hour = Math.Clamp(opts.AutoRunLocalHour, 0, 23);
                var minute = Math.Clamp(opts.AutoRunLocalMinute, 0, 59);
                var scheduledToday = new DateTimeOffset(nowLocal.Year, nowLocal.Month, nowLocal.Day, hour, minute, 0, offset);
                if (nowLocal < scheduledToday)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (_skippedOrFailedRaceDate == raceDate)
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var callerId = opts.Admins[0];
                var outcome = await horse.RunRaceAsync(callerId, HorseRunKind.Global, 0, stoppingToken).ConfigureAwait(false);

                switch (outcome.Error)
                {
                    case HorseError.None:
                        LogAutoRunOk(raceDate, outcome.Winner + 1);
                        break;
                    case HorseError.NotEnoughBets:
                        _skippedOrFailedRaceDate = raceDate;
                        LogAutoRunSkippedInsufficientBets(raceDate, opts.MinBetsToRun);
                        break;
                    default:
                        _skippedOrFailedRaceDate = raceDate;
                        LogAutoRunFailed(outcome.Error);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogAutoRunException(ex);
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(EventId = 2850, Level = LogLevel.Warning,
        Message = "horse.auto_run skipped: Games:horse:Admins is empty")]
    private partial void LogAutoRunNoAdmins();

    [LoggerMessage(EventId = 2851, Level = LogLevel.Information,
        Message = "horse.auto_run ok race_date={RaceDate} winner_horse={Winner}")]
    private partial void LogAutoRunOk(string raceDate, int winner);

    [LoggerMessage(EventId = 2852, Level = LogLevel.Information,
        Message = "horse.auto_run skipped: not enough bets race_date={RaceDate} need>={MinBets}")]
    private partial void LogAutoRunSkippedInsufficientBets(string raceDate, int minBets);

    [LoggerMessage(EventId = 2853, Level = LogLevel.Warning,
        Message = "horse.auto_run failed error={Error}")]
    private partial void LogAutoRunFailed(HorseError error);

    [LoggerMessage(EventId = 2854, Level = LogLevel.Error,
        Message = "horse.auto_run exception")]
    private partial void LogAutoRunException(Exception ex);
}
