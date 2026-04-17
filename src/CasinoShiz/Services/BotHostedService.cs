using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace CasinoShiz.Services;

public sealed partial class BotHostedService(
    IServiceProvider serviceProvider,
    IOptions<BotOptions> options,
    ILogger<BotHostedService> logger) : IHostedService
{
    private readonly BotOptions _options = options.Value;
    private CancellationTokenSource? _cts;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
        }

        using (var scope = serviceProvider.CreateScope())
        {
            var reporter = scope.ServiceProvider.GetRequiredService<ClickHouseReporter>();
            await reporter.CreateTableIfNotExists();
        }

        var botClient = serviceProvider.GetRequiredService<ITelegramBotClient>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_options.IsProduction)
        {
            LogStartingBotInWebhookModeOnPortPort(_options.WebhookPort);
        }
        else
        {
            LogStartingBotInPollingMode();
            _ = Task.Run(() => StartPolling(botClient, _cts.Token), _cts.Token);
        }
    }

    private async Task StartPolling(ITelegramBotClient botClient, CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await botClient.GetUpdates(offset, timeout: 30, cancellationToken: ct);
                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    using var scope = serviceProvider.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
                    await handler.HandleUpdateAsync(botClient, update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogErrorDuringPolling(ex);
                await Task.Delay(1000, ct);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        LogBotStopped();
        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Starting bot in webhook mode on port {Port}")]
    partial void LogStartingBotInWebhookModeOnPortPort(int port);

    [LoggerMessage(LogLevel.Information, "Starting bot in polling mode")]
    partial void LogStartingBotInPollingMode();

    [LoggerMessage(LogLevel.Error, "Error during polling")]
    partial void LogErrorDuringPolling(Exception exception);

    [LoggerMessage(LogLevel.Information, "Bot stopped")]
    partial void LogBotStopped();
}
