namespace CasinoShiz.Host.Debug;

/// <summary>Process start time for uptime in <see cref="DebugHandler"/>.</summary>
public sealed class BotProcessClock
{
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
}
