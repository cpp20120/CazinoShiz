using System.Collections.Concurrent;

namespace BotFramework.Host.Composition;

public interface IBackgroundJobStatusService
{
    IReadOnlyList<BackgroundJobStatusSnapshot> Snapshot();
    void Register(string jobName);
    void MarkStarting(string jobName);
    void MarkRunning(string jobName);
    void MarkCompleted(string jobName);
    void MarkCrashed(string jobName, Exception exception, int backoffMs);
    void MarkStopped(string jobName);
}

public sealed record BackgroundJobStatusSnapshot(
    string Name,
    string State,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastFailedAt,
    int CrashCount,
    int? RestartBackoffMs,
    string? LastError);

public sealed class BackgroundJobStatusService : IBackgroundJobStatusService
{
    private readonly ConcurrentDictionary<string, MutableStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BackgroundJobStatusSnapshot> Snapshot() =>
        _statuses.Values
            .Select(static s => s.ToSnapshot())
            .OrderBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Register(string jobName) =>
        _statuses.GetOrAdd(jobName, static name => new MutableStatus(name));

    public void MarkStarting(string jobName)
    {
        var s = Get(jobName);
        lock (s)
        {
            var now = DateTimeOffset.UtcNow;
            s.State = "starting";
            s.LastStartedAt = now;
            s.LastHeartbeatAt = now;
            s.RestartBackoffMs = null;
        }
    }

    public void MarkRunning(string jobName)
    {
        var s = Get(jobName);
        lock (s)
        {
            s.State = "running";
            s.LastHeartbeatAt = DateTimeOffset.UtcNow;
            s.RestartBackoffMs = null;
        }
    }

    public void MarkCompleted(string jobName)
    {
        var s = Get(jobName);
        lock (s)
        {
            var now = DateTimeOffset.UtcNow;
            s.State = "completed";
            s.LastHeartbeatAt = now;
            s.LastCompletedAt = now;
            s.RestartBackoffMs = null;
        }
    }

    public void MarkCrashed(string jobName, Exception exception, int backoffMs)
    {
        var s = Get(jobName);
        lock (s)
        {
            var now = DateTimeOffset.UtcNow;
            s.State = "crashed";
            s.LastHeartbeatAt = now;
            s.LastFailedAt = now;
            s.CrashCount++;
            s.RestartBackoffMs = backoffMs;
            s.LastError = exception.GetType().Name + ": " + exception.Message;
        }
    }

    public void MarkStopped(string jobName)
    {
        var s = Get(jobName);
        lock (s)
        {
            s.State = "stopped";
            s.LastHeartbeatAt = DateTimeOffset.UtcNow;
            s.RestartBackoffMs = null;
        }
    }

    private MutableStatus Get(string jobName) =>
        _statuses.GetOrAdd(jobName, static name => new MutableStatus(name));

    private sealed class MutableStatus(string name)
    {
        public string Name { get; } = name;
        public string State { get; set; } = "registered";
        public DateTimeOffset? LastStartedAt { get; set; }
        public DateTimeOffset? LastHeartbeatAt { get; set; }
        public DateTimeOffset? LastCompletedAt { get; set; }
        public DateTimeOffset? LastFailedAt { get; set; }
        public int CrashCount { get; set; }
        public int? RestartBackoffMs { get; set; }
        public string? LastError { get; set; }

        public BackgroundJobStatusSnapshot ToSnapshot()
        {
            lock (this)
            {
                return new BackgroundJobStatusSnapshot(
                    Name,
                    State,
                    LastStartedAt,
                    LastHeartbeatAt,
                    LastCompletedAt,
                    LastFailedAt,
                    CrashCount,
                    RestartBackoffMs,
                    LastError);
            }
        }
    }
}
