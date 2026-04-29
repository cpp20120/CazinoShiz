using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotFramework.Host.Composition;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Games.PixelBattle;

public sealed class TelegramWebAppInitDataValidator(
    IOptions<BotFrameworkOptions> botOptions,
    IOptions<PixelBattleOptions> pixelOptions,
    TimeProvider? timeProvider = null) : ITelegramWebAppInitDataValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public bool TryValidate(string? initData, out TelegramWebAppAuth auth)
    {
        auth = default!;

        var botToken = botOptions.Value.Token;
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(initData))
            return false;

        var parameters = QueryHelpers.ParseQuery(initData);
        if (!parameters.TryGetValue("hash", out var hashValues))
            return false;

        var hash = hashValues.ToString();
        if (string.IsNullOrWhiteSpace(hash))
            return false;

        var dataCheckString = BuildDataCheckString(parameters);
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var calculated = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));

        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(hash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(calculated, supplied))
            return false;

        if (!TryGetAuthDate(parameters, out var authDate))
            return false;

        var now = _timeProvider.GetUtcNow();
        if (authDate > now.AddMinutes(1) || now - authDate > pixelOptions.Value.MaxInitDataAge)
            return false;

        if (!parameters.TryGetValue("user", out var userValues))
            return false;

        TelegramWebAppUser? user;
        try
        {
            user = JsonSerializer.Deserialize<TelegramWebAppUser>(userValues.ToString(), JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (user is not { Id: > 0 })
            return false;

        auth = new TelegramWebAppAuth(user, authDate);
        return true;
    }

    private static string BuildDataCheckString(Dictionary<string, StringValues> parameters) =>
        string.Join('\n', parameters
            .Where(pair => pair.Key != "hash")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));

    private static bool TryGetAuthDate(Dictionary<string, StringValues> parameters, out DateTimeOffset authDate)
    {
        authDate = default;
        if (!parameters.TryGetValue("auth_date", out var authDateValues))
            return false;

        return long.TryParse(authDateValues.ToString(), out var unixSeconds)
            && unixSeconds > 0
            && TryFromUnixTimeSeconds(unixSeconds, out authDate);
    }

    private static bool TryFromUnixTimeSeconds(long unixSeconds, out DateTimeOffset authDate)
    {
        try
        {
            authDate = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            authDate = default;
            return false;
        }
    }
}
