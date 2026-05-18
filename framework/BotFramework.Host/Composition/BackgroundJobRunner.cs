// ─────────────────────────────────────────────────────────────────────────────
// BackgroundJobRunner — hosts every module-registered IBackgroundJob. Each
// job runs in its own supervised Task with restart-on-crash + exponential
// backoff, mirroring BotHostedService's polling supervisor. A job crash
// never brings the Host down.
//
// Shutdown: cancels the shared loop CTS, waits for every job task to observe
// the cancellation, then returns. Jobs that fail to exit inside the
// stopToken window are logged and left — the Generic Host will call
// Environment.Exit after the stop timeout anyway.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace BotFramework.Host.Composition;

public sealed partial class BackgroundJobRunner(
    ModuleRegistrations registrations,
    IServiceProvider services,
    IBackgroundJobStatusService statuses,
    ILogger<BackgroundJobRunner> logger) : IHostedService
{
    private readonly List<Task> _jobTasks = [];
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        foreach (var job in registrations.BackgroundJobs.Select(jobType => (IBackgroundJob)services.GetRequiredService(jobType)))
        {
            statuses.Register(job.Name);
            statuses.MarkStarting(job.Name);
            LogStartingJob(job.Name);
            _jobTasks.Add(Task.Run(() => RunWithSupervisionAsync(job, _cts.Token), _cts.Token));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts?.CancelAsync();
        if (_jobTasks.Count == 0) return;
        try { await Task.WhenAll(_jobTasks).WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogStopFailed(ex); }
    }

    private async Task RunWithSupervisionAsync(IBackgroundJob job, CancellationToken ct)
    {
        var backoffMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                statuses.MarkRunning(job.Name);
                await job.RunAsync(ct);
                statuses.MarkCompleted(job.Name);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                statuses.MarkStopped(job.Name);
                return;
            }
            catch (Exception ex)
            {
                statuses.MarkCrashed(job.Name, ex, backoffMs);
                LogJobCrashed(job.Name, ex, backoffMs);
                try { await Task.Delay(backoffMs, ct); } catch (OperationCanceledException) { statuses.MarkStopped(job.Name); return; }
                backoffMs = Math.Min(backoffMs * 2, 60_000);
                statuses.MarkStarting(job.Name);
            }
        }
    }

    [LoggerMessage(LogLevel.Information, "background_job.starting name={JobName}")]
    partial void LogStartingJob(string jobName);

    [LoggerMessage(LogLevel.Error, "background_job.crashed name={JobName} restarting_after_ms={BackoffMs}")]
    partial void LogJobCrashed(string jobName, Exception exception, int backoffMs);

    [LoggerMessage(LogLevel.Warning, "background_job.stop_failed")]
    partial void LogStopFailed(Exception exception);
}