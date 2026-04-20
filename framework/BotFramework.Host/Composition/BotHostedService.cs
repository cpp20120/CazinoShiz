// ─────────────────────────────────────────────────────────────────────────────
// BotHostedService — the Telegram update driver.
//
// Two modes, controlled by BotFrameworkOptions.IsProduction:
//   • polling (dev): long-poll Telegram's GetUpdates in a supervised loop,
//     dispatch each update through UpdatePipeline in a new DI scope.
//   • webhook (prod): this service stays idle; the webhook HTTP endpoint
//     (mapped by UseBotFramework()) dispatches arriving updates directly.
//
// StartAsync also:
//   • logs the router's route table at INFO (operators want to see what's
//     registered on boot)
//   • calls SetMyCommands with every module's aggregated BotCommand list so
//     the Telegram client's "/" menu lists every registered command
//
// Migration runs separately (ModuleMigrationRunner is its own IHostedService
// registered before this one, so schema is ready when we start polling).
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host.Composition;
using BotFramework.Host.Pipeline;
using BotFramework.Sdk;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using TgBotCommand = Telegram.Bot.Types.BotCommand;

namespace BotFramework.Host.Composition;

public sealed partial class BotHostedService(
    IServiceProvider serviceProvider,
    ITelegramBotClient botClient,
    LoadedModules loadedModules,
    UpdateRouter router,
    IOptions<BotFrameworkOptions> options,
    ILogger<BotHostedService> logger) : IHostedService
{
    private readonly BotFrameworkOptions _options = options.Value;
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        router.LogRegisteredRoutes();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await RegisterCommandsAsync(_cts.Token);

        if (_options.IsProduction)
        {
            LogStartingBotInWebhookMode(_options.WebhookPort);
        }
        else
        {
            LogStartingBotInPollingMode();
            _pollingTask = Task.Run(() => RunPollingWithSupervision(_cts.Token), _cts.Token);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_pollingTask is not null)
        {
            try { await _pollingTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
        LogBotStopped();
    }

    private async Task RegisterCommandsAsync(CancellationToken ct)
    {
        if (loadedModules.BotCommands.Count == 0) return;

        var tg = loadedModules.BotCommands
            .Select(c => new TgBotCommand { Command = c.Command, Description = c.DescriptionKey })
            .ToArray();

        try
        {
            await botClient.SetMyCommands(tg, cancellationToken: ct);
            LogRegisteredBotCommands(tg.Length);
        }
        catch (Exception ex)
        {
            LogFailedToRegisterCommands(ex);
        }
    }

    private async Task RunPollingWithSupervision(CancellationToken ct)
    {
        var backoffMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunPollingLoop(ct);
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

    private async Task RunPollingLoop(CancellationToken ct)
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
                    var pipeline = scope.ServiceProvider.GetRequiredService<UpdatePipeline>();
                    var ctx = new UpdateContext(botClient, update, scope.ServiceProvider, ct);
                    await pipeline.InvokeAsync(ctx);
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
