using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Data.Entities;
using CasinoShiz.Services.Handlers;
using CasinoShiz.Services.Poker.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace CasinoShiz.Services;

public sealed partial class PokerTurnTimeoutService(
    IServiceProvider serviceProvider,
    IOptions<BotOptions> options,
    ILogger<PokerTurnTimeoutService> logger) : BackgroundService
{
    private readonly BotOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var service = scope.ServiceProvider.GetRequiredService<PokerService>();
        var handler = scope.ServiceProvider.GetRequiredService<PokerHandler>();

        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _opts.PokerTurnTimeoutMs;

        var stuck = await db.PokerTables
            .Where(t => t.Status == PokerTableStatus.HandActive && t.LastActionAt < cutoff)
            .Select(t => t.InviteCode)
            .ToListAsync(ct);

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
