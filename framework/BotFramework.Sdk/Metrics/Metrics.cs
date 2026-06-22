// ─────────────────────────────────────────────────────────────────────────────
// Metrics abstraction.
//
// The SDK surface is deliberately dumb: counter + histogram + gauge, tagged
// by string labels. Modules use this to emit per-game metrics without
// importing Prometheus / OpenTelemetry / whatever the backend is this quarter.
//
// The Host decides the backend. For a real deployment it's Prometheus client
// libraries + scrape endpoint. For local dev it's an in-memory store the
// admin UI visualizes. For tests it's a fake that lets assertions be written.
//
// Naming convention: bot_<domain>_<what>[_unit]. bot_commands_total,
// bot_command_duration_ms, bot_active_sessions. Prometheus "total" + "_ms"
// idioms bake in; operators will thank us for consistency.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;

public interface IMetrics
{
    void CounterInc(string name, IReadOnlyDictionary<string, string>? tags = null, double amount = 1);
    void HistogramObserve(string name, double value, IReadOnlyDictionary<string, string>? tags = null);
    void GaugeSet(string name, double value, IReadOnlyDictionary<string, string>? tags = null);
}
