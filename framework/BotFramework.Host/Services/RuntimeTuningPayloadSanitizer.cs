using System.Text.Json.Nodes;

namespace BotFramework.Host.Services;

/// <summary>Whitelists JSON keys allowed in <c>runtime_tuning.payload</c> (admin-edited overlay).</summary>
public static class RuntimeTuningPayloadSanitizer
{
    public static readonly HashSet<string> AllowedBotKeys = new(StringComparer.Ordinal)
    {
        "DailyBonus",
        "TelegramDiceDailyLimit",
    };

    public static readonly HashSet<string> AllowedGameKeys = new(StringComparer.Ordinal)
    {
        "dice", "dicecube", "darts", "football", "basketball", "bowling", "horse", "poker", "transfer",
    };

    public static JsonObject Sanitize(JsonObject raw)
    {
        var o = new JsonObject();
        if (raw["Bot"] is JsonObject bot)
        {
            var b = new JsonObject();
            foreach (var p in bot)
            {
                if (AllowedBotKeys.Contains(p.Key))
                    b[p.Key] = p.Value?.DeepClone();
            }

            if (b.Count > 0) o["Bot"] = b;
        }

        if (raw["Games"] is JsonObject games)
        {
            var g = new JsonObject();
            foreach (var p in games)
            {
                if (AllowedGameKeys.Contains(p.Key))
                    g[p.Key] = p.Value?.DeepClone();
            }

            if (g.Count > 0) o["Games"] = g;
        }

        return o;
    }
}
