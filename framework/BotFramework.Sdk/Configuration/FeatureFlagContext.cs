// ─────────────────────────────────────────────────────────────────────────────
// Feature flags — runtime toggles with no redeploy.
//
// Why a first-class contract and not "just read IConfiguration":
//   • flags change without restart — config binding caches values; flag
//     provider explicitly re-queries on every check (or implements its own
//     cache with a short TTL)
//   • per-user / per-culture rollouts need a richer key than "section name"
//   • audit: who flipped "sh.enabled" to false, when, and why — flag provider
//     owns that trail; config provider doesn't
//
// Sketch surface is deliberately narrow. Real implementations point at
// GrowthBook, LaunchDarkly, a postgres table, or a local JSON file — the
// IFeatureFlags interface hides all of that.
// ─────────────────────────────────────────────────────────────────────────────

namespace BotFramework.Sdk;
/// Context used for per-user / per-culture rollouts. All fields optional —
/// the provider picks what it needs. Keeping this narrow keeps the flag
/// backend pluggable.
public sealed record FeatureFlagContext(
    long? UserId = null,
    string? CultureCode = null,
    IReadOnlyDictionary<string, string>? Attributes = null);
