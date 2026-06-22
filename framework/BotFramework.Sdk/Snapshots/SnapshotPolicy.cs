// ─────────────────────────────────────────────────────────────────────────────
// Snapshots — performance escape hatch for long-lived event-sourced aggregates.
//
// Problem: EventStoreRepository.FindAsync replays every event in the stream
// on every load. For a Secret Hitler room with 200 events, that's fine. For
// a poker cash-table that's been running all night with 50,000 events, it's
// not. Worse, replay cost scales with game duration — bad for UX and cost.
//
// Fix: every N commands (or on some trigger like "game ended"), persist a
// snapshot of the aggregate's state. On load, grab the newest snapshot,
// hydrate the aggregate from it, then replay only events AFTER the snapshot's
// version. Replay cost is bounded by snapshot frequency, not game length.
//
// Snapshot cadence policy lives in the Host, not the module — one knob per
// module via SnapshotOptions { every = 100 events } keeps operators in
// control without leaking into domain code.
//
// Why not snapshot after every event:
//   • defeats the purpose; snapshot store fills up the same as the event store
//   • snapshots are a cache, not a source of truth — only the event stream is
//     authoritative. Corrupted snapshot → delete it, replay rebuilds the state
//     losslessly.
//
// Schema: module_snapshots with (stream_id, version, state_json, taken_at).
// One row per aggregate — we UPSERT, not INSERT. No history retention on
// snapshots; the event stream is the history.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Per-module cadence policy. A cash-poker table with thousands of events per
/// hour wants Every = 200; a Secret Hitler room with dozens of events per
/// game wants snapshotting disabled (SnapshotEvery = 0 → always full replay).
public sealed record SnapshotPolicy(int Every)
{
    public static SnapshotPolicy Disabled { get; } = new(0);
    public bool ShouldSnapshot(long version) => Every > 0 && version % Every == 0;
}
