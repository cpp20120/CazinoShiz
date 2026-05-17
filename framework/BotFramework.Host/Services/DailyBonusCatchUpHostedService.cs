using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotFramework.Host.Services;

public sealed partial class DailyBonusCatchUpHostedService(
    IDailyBonusService dailyBonus,
    IRuntimeTuningAccessor tuning,
    ILogger<DailyBonusCatchUpHostedService> logger) : IHostedService
{
    private Task? _runTask;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null || _runTask is null) return;
        await _cts.CancelAsync();
        try { await _runTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await RunOnceAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            var delay = DelayUntilNextLocalCatchUp(tuning.DailyBonus.TimezoneOffsetHours);
            LogNextRun((int)delay.TotalSeconds);
            await Task.Delay(delay, ct);
            await RunOnceAsync(ct);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stats = await dailyBonus.CatchUpMissedDaysAsync(cancellationToken);
            if (stats.Wallets > 0 || stats.Days > 0 || stats.CreditedCoins > 0)
                LogCompleted(stats.Wallets, stats.Days, stats.CreditedCoins, stats.SkippedDays);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(ex);
        }
    }

    private static TimeSpan DelayUntilNextLocalCatchUp(int timezoneOffsetHours)
    {
        var offset = TimeSpan.FromHours(timezoneOffsetHours);
        var now = DateTimeOffset.UtcNow.ToOffset(offset);
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 5, 0, offset).AddDays(1);
        var delay = next - now;
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }

    [LoggerMessage(LogLevel.Information, "daily_bonus.catchup.completed wallets={Wallets} days={Days} credited={CreditedCoins} skipped={SkippedDays}")]
    partial void LogCompleted(int wallets, int days, int creditedCoins, int skippedDays);

    [LoggerMessage(LogLevel.Information, "daily_bonus.catchup.next_run_after_sec={DelaySeconds}")]
    partial void LogNextRun(int delaySeconds);

    [LoggerMessage(LogLevel.Error, "daily_bonus.catchup.failed")]
    partial void LogFailed(Exception exception);
}
