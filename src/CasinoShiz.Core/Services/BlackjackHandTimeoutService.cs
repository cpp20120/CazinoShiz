using CasinoShiz.Configuration;
using CasinoShiz.Data;
using CasinoShiz.Services.Blackjack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CasinoShiz.Services;

public sealed partial class BlackjackHandTimeoutService(
    IServiceProvider serviceProvider,
    IOptions<BotOptions> options,
    ILogger<BlackjackHandTimeoutService> logger) : BackgroundService
{
    private readonly BotOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(5_000, stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) { LogBlackjackTimeoutSweepFailed(ex); }

            try { await Task.Delay(30_000, stoppingToken); } catch { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<BlackjackService>();

        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _opts.BlackjackHandTimeoutMs;

        var stuckUserIds = await db.BlackjackHands
            .Where(h => h.CreatedAt < cutoff)
            .Select(h => h.UserId)
            .ToListAsync(ct);

        foreach (var userId in stuckUserIds)
        {
            try
            {
                await service.StandAsync(userId, ct);
                LogBlackjackTimeoutFired(userId);
            }
            catch (Exception ex)
            {
                LogBlackjackTimeoutActionFailed(userId, ex);
            }
        }
    }

    [LoggerMessage(LogLevel.Error, "blackjack.timeout.sweep failed")]
    partial void LogBlackjackTimeoutSweepFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "blackjack.timeout.fired user={UserId}")]
    partial void LogBlackjackTimeoutFired(long userId);

    [LoggerMessage(LogLevel.Warning, "blackjack.timeout.action_failed user={UserId}")]
    partial void LogBlackjackTimeoutActionFailed(long userId, Exception exception);
}
