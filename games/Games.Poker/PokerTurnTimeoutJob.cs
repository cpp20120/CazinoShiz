// ─────────────────────────────────────────────────────────────────────────────
// PokerTurnTimeoutJob — sweeps tables whose current seat hasn't acted within
// PokerOptions.TurnTimeoutMs. Each idle table gets an auto-action (check if
// nothing to call, otherwise fold) applied and a notification broadcast.
//
// Ported from src/CasinoShiz.Core/Services/PokerTurnTimeoutService.cs. The
// 5-second warm-up gives the host time to finish migrations before we start
// poking at tables.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using BotFramework.Host.Services;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace Games.Poker;

public sealed partial class PokerTurnTimeoutJob(
    IServiceProvider services,
    IRuntimeTuningAccessor tuning,
    ILogger<PokerTurnTimeoutJob> logger) : IBackgroundJob
{
    public string Name => "poker.turn_timeout";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(5_000, stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) { LogPokerTimeoutSweepFailed(ex); }

            try { await Task.Delay(10_000, stoppingToken); } catch { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var opts = tuning.GetSection<PokerOptions>(PokerOptions.SectionName);
        PokerService.PruneGates((long)opts.TurnTimeoutMs * 3);

        using var scope = services.CreateScope();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var service = scope.ServiceProvider.GetRequiredService<IPokerService>();
        var handler = scope.ServiceProvider.GetRequiredService<PokerHandler>();

        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - opts.TurnTimeoutMs;
        var stuck = await service.ListStuckCodesAsync(cutoff, ct);

        foreach (var code in stuck)
        {
            try
            {
                var result = await service.RunAutoActionAsync(code, ct);
                if (result != null)
                {
                    LogPokerTimeoutFired(code, result.Transition);
                    await handler.BroadcastAutoActionAsync(bot, result, ct);
                }
            }
            catch (Exception ex)
            {
                LogPokerTimeoutActionFailed(code, ex);
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "poker.timeout.sweep failed")]
    partial void LogPokerTimeoutSweepFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "poker.timeout.fired code={Code} transition={T}")]
    partial void LogPokerTimeoutFired(string code, HandTransition T);

    [LoggerMessage(LogLevel.Warning, "poker.timeout.action_failed code={Code}")]
    partial void LogPokerTimeoutActionFailed(string code, Exception exception);
}
