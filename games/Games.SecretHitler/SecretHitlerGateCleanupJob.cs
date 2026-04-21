using BotFramework.Sdk;

namespace Games.SecretHitler;

public sealed partial class SecretHitlerGateCleanupJob : IBackgroundJob
{
    private const long IdleMs = 60 * 60 * 1_000; // 1 hour

    public string Name => "sh.gate_cleanup";

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(5_000, stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            SecretHitlerService.PruneGates(IdleMs);

            try { await Task.Delay(10 * 60 * 1_000, stoppingToken); } catch { return; }
        }
    }
}
