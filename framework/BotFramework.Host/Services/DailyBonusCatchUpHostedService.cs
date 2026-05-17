using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotFramework.Host.Services;

public sealed partial class DailyBonusCatchUpHostedService(
    IDailyBonusService dailyBonus,
    ILogger<DailyBonusCatchUpHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(LogLevel.Information, "daily_bonus.catchup.completed wallets={Wallets} days={Days} credited={CreditedCoins} skipped={SkippedDays}")]
    partial void LogCompleted(int wallets, int days, int creditedCoins, int skippedDays);

    [LoggerMessage(LogLevel.Error, "daily_bonus.catchup.failed")]
    partial void LogFailed(Exception exception);
}
