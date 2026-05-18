using System.Collections.Concurrent;

namespace BotFramework.Host.Composition;

public interface IBackgroundJobStatusService
{
    IReadOnlyList<BackgroundJobStatusSnapshot> Snapshot();
    void Register(string jobName, string kind = "module");
    void MarkStarting(string jobName);
    void MarkRunning(string jobName);
    void MarkCompleted(string jobName);
    void MarkCrashed(string jobName, Exception exception, int backoffMs);
    void MarkFailed(string jobName, Exception exception);
    void MarkWaiting(string jobName, DateTimeOffset nextRunAt, string? note = null);
    void MarkStopped(string jobName);
}

public sealed record BackgroundJobStatusSnapshot(
    string Name,
    string Kind,
    string State,
    DateTimeOffset? LastStartedAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastCompletedAt,
    DateTimeOffset? LastFailedAt,
    DateTimeOffset? NextRunAt,
    int CrashCount,
    int? RestartBackoffMs,
    string? LastError,
    string? Note);

public sealed class BackgroundJobStatusService : IBackgroundJobStatusService
{
    private readonly ConcurrentDictionary<string, MutableStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BackgroundJobStatusSnapshot> Snapshot() =>
        _statuses.Values
            .Select(static s => s.ToSnapshot())
            .OrderBy(static s => s.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void Register(string jobName, string kind = "module")
    {
        var s = Get(jobName);
        lock (s)
        {
            s.Kind = kind;
        }
    }

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
            s.NextRunAt = null;
            s.Note = null;
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
            s.NextRunAt = null;
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

    public void MarkFailed(string jobName, Exception exception)
    {
        var s = Get(jobName);
        lock (s)
        {
            var now = DateTimeOffset.UtcNow;
            s.State = "failed";
            s.LastHeartbeatAt = now;
            s.LastFailedAt = now;
            s.CrashCount++;
            s.RestartBackoffMs = null;
            s.LastError = exception.GetType().Name + ": " + exception.Message;
        }
    }

    public void MarkWaiting(string jobName, DateTimeOffset nextRunAt, string? note = null)
    {
        var s = Get(jobName);
        lock (s)
        {
            s.State = "waiting";
            s.LastHeartbeatAt = DateTimeOffset.UtcNow;
            s.NextRunAt = nextRunAt;
            s.RestartBackoffMs = null;
            s.Note = note;
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
            s.NextRunAt = null;
        }
    }

    private MutableStatus Get(string jobName) =>
        _statuses.GetOrAdd(jobName, static name => new MutableStatus(name));

    private sealed class MutableStatus(string name)
    {
        public string Name { get; } = name;
        public string Kind { get; set; } = "module";
        public string State { get; set; } = "registered";
        public DateTimeOffset? LastStartedAt { get; set; }
        public DateTimeOffset? LastHeartbeatAt { get; set; }
        public DateTimeOffset? LastCompletedAt { get; set; }
        public DateTimeOffset? LastFailedAt { get; set; }
        public DateTimeOffset? NextRunAt { get; set; }
        public int CrashCount { get; set; }
        public int? RestartBackoffMs { get; set; }
        public string? LastError { get; set; }
        public string? Note { get; set; }

        public BackgroundJobStatusSnapshot ToSnapshot()
        {
            lock (this)
            {
                return new BackgroundJobStatusSnapshot(
                    Name,
                    Kind,
                    State,
                    LastStartedAt,
                    LastHeartbeatAt,
                    LastCompletedAt,
                    LastFailedAt,
                    NextRunAt,
                    CrashCount,
                    RestartBackoffMs,
                    LastError,
                    Note);
            }
        }
    }
}