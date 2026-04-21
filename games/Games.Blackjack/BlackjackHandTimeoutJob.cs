// ─────────────────────────────────────────────────────────────────────────────
// BlackjackHandTimeoutJob — sweeps abandoned hands every 30 seconds. An
// idle hand is auto-stood (settled on whatever the player currently has),
// releasing the UserState slot so the player can start a new hand.
//
// Ported from src/CasinoShiz.Core/Services/BlackjackHandTimeoutService.cs;
// rewired as an IBackgroundJob the framework hosts. The 5-second warm-up
// delay survives so we don't sweep on boot before the DB has even opened.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Games.Blackjack;

public sealed partial class BlackjackHandTimeoutJob(
    IServiceProvider services,
    IOptions<BlackjackOptions> options,
    ILogger<BlackjackHandTimeoutJob> logger) : IBackgroundJob
{
    private readonly BlackjackOptions _opts = options.Value;

    public string Name => "blackjack.hand_timeout";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(5_000, stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (Exception ex) { LogSweepFailed(ex); }

            try { await Task.Delay(30_000, stoppingToken); } catch { return; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        BlackjackService.PruneGates((long)_opts.HandTimeoutMs);

        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IBlackjackHandStore>();
        var service = scope.ServiceProvider.GetRequiredService<IBlackjackService>();

        var cutoff = DateTimeOffset.UtcNow.AddMilliseconds(-_opts.HandTimeoutMs);
        var stuck = await store.ListStuckUserIdsAsync(cutoff, ct);

        foreach (var userId in stuck)
        {
            try
            {
                await service.StandAsync(userId, ct);
                LogTimeoutFired(userId);
            }
            catch (Exception ex) { LogTimeoutActionFailed(userId, ex); }
        }
    }

    [LoggerMessage(LogLevel.Error, "blackjack.timeout.sweep failed")]
    partial void LogSweepFailed(Exception exception);

    [LoggerMessage(LogLevel.Information, "blackjack.timeout.fired user={UserId}")]
    partial void LogTimeoutFired(long userId);

    [LoggerMessage(LogLevel.Warning, "blackjack.timeout.action_failed user={UserId}")]
    partial void LogTimeoutActionFailed(long userId, Exception exception);
}
