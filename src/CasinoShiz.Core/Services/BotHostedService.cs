using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services.Analytics;
using CasinoShiz.Services.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

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

        using (var scope = serviceProvider.CreateScope())
        {
            var router = scope.ServiceProvider.GetRequiredService<UpdateRouter>();
            router.LogRegisteredRoutes();
        }

        var botClient = serviceProvider.GetRequiredService<ITelegramBotClient>();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await RegisterCommandsAsync(botClient, _cts.Token);

        if (_options.IsProduction)
        {
            LogStartingBotInWebhookMode(_options.WebhookPort);
        }
        else
        {
            LogStartingBotInPollingMode();
            _ = Task.Run(() => RunPollingWithSupervision(botClient, _cts.Token), _cts.Token);
        }
    }

    private async Task RunPollingWithSupervision(ITelegramBotClient botClient, CancellationToken ct)
    {
        var backoffMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StartPolling(botClient, ct);
                backoffMs = 1000;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogPollingLoopCrashed(ex, backoffMs);
                try { await Task.Delay(backoffMs, ct); } catch (OperationCanceledException) { return; }
                backoffMs = Math.Min(backoffMs * 2, 60_000);
            }
        }
    }

    private async Task RegisterCommandsAsync(ITelegramBotClient botClient, CancellationToken ct)
    {
        var commands = new BotCommand[]
        {
            new() { Command = "blackjack", Description = "Сыграть в блэкджек" },
            new() { Command = "poker", Description = "Техасский холдем" },
            new() { Command = "horse", Description = "Скачки" },
            new() { Command = "redeem", Description = "Ввести промокод на фриспины" },
            new() { Command = "top", Description = "Таблица лидеров" },
            new() { Command = "balance", Description = "Мой баланс" },
            new() { Command = "help", Description = "Список команд" },
        };

        try
        {
            await botClient.SetMyCommands(commands, cancellationToken: ct);
            LogRegisteredBotCommands(commands.Length);
        }
        catch (Exception ex)
        {
            LogFailedToRegisterCommands(ex);
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
    partial void LogStartingBotInWebhookMode(int port);

    [LoggerMessage(LogLevel.Information, "Starting bot in polling mode")]
    partial void LogStartingBotInPollingMode();

    [LoggerMessage(LogLevel.Error, "Error during polling")]
    partial void LogErrorDuringPolling(Exception exception);

    [LoggerMessage(LogLevel.Information, "Bot stopped")]
    partial void LogBotStopped();

    [LoggerMessage(LogLevel.Information, "Registered {Count} bot commands in Telegram menu")]
    partial void LogRegisteredBotCommands(int count);

    [LoggerMessage(LogLevel.Warning, "Failed to register bot commands")]
    partial void LogFailedToRegisterCommands(Exception exception);

    [LoggerMessage(LogLevel.Error, "Polling loop crashed, restarting after {BackoffMs}ms")]
    partial void LogPollingLoopCrashed(Exception exception, int backoffMs);
}
