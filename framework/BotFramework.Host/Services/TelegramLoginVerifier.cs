using System.Security.Cryptography;
using System.Text;

namespace BotFramework.Host.Services;

/// Verifies Telegram Login Widget data per https://core.telegram.org/widgets/login
public sealed class TelegramLoginVerifier(string botToken)
{
    private readonly byte[] _secretKey = SHA256.HashData(Encoding.UTF8.GetBytes(botToken));

    public bool Verify(IReadOnlyDictionary<string, string> fields, out long userId, out string name)
    {
        userId = 0;
        name = "";

        if (!fields.TryGetValue("hash", out var hash) ||
            !fields.TryGetValue("id", out var idStr) ||
            !long.TryParse(idStr, out userId))
            return false;

        if (fields.TryGetValue("first_name", out var fn))
            name = fields.TryGetValue("last_name", out var ln) ? $"{fn} {ln}".Trim() : fn;

        // Check freshness: auth_date must be within 86400 seconds
        if (!fields.TryGetValue("auth_date", out var authDateStr) ||
            !long.TryParse(authDateStr, out var authDate) ||
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - authDate > 86_400)
            return false;

        var dataCheckString = fields
            .Where(kv => kv.Key != "hash")
            .OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .Aggregate((a, b) => $"{a}\n{b}");

        using var hmac = new HMACSHA256(_secretKey);
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));
        return string.Equals(computed, hash, StringComparison.OrdinalIgnoreCase);
    }
}
