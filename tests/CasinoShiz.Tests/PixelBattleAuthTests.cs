using System.Security.Cryptography;
using System.Text;
using BotFramework.Host.Composition;
using Games.PixelBattle;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class PixelBattleAuthTests
{
    private const string BotToken = "123456:test_token";

    [Fact]
    public void ValidInitDataReturnsUser()
    {
        var initData = BuildInitData(BotToken, DateTimeOffset.UtcNow, """{"id":42,"first_name":"Ada","username":"ada"}""");
        var validator = CreateValidator();

        var ok = validator.TryValidate(initData, out var auth);

        Assert.True(ok);
        Assert.Equal(42, auth.User.Id);
        Assert.Equal("Ada", auth.User.FirstName);
        Assert.Equal("ada", auth.User.Username);
    }

    [Fact]
    public void TamperedInitDataIsRejected()
    {
        var initData = BuildInitData(BotToken, DateTimeOffset.UtcNow, """{"id":42,"first_name":"Ada"}""")
            .Replace("Ada", "Mallory", StringComparison.Ordinal);
        var validator = CreateValidator();

        var ok = validator.TryValidate(initData, out _);

        Assert.False(ok);
    }

    [Fact]
    public void StaleInitDataIsRejected()
    {
        var initData = BuildInitData(
            BotToken,
            DateTimeOffset.UtcNow.AddDays(-2),
            """{"id":42,"first_name":"Ada"}""");
        var validator = CreateValidator();

        var ok = validator.TryValidate(initData, out _);

        Assert.False(ok);
    }

    private static TelegramWebAppInitDataValidator CreateValidator() =>
        new(
            Options.Create(new BotFrameworkOptions { Token = BotToken }),
            Options.Create(new PixelBattleOptions { MaxInitDataAgeSeconds = 86_400 }));

    private static string BuildInitData(string botToken, DateTimeOffset authDate, string userJson)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = authDate.ToUnixTimeSeconds().ToString(),
            ["query_id"] = "AAHdF6IQAAAAAN0XohDhrOrc",
            ["user"] = userJson,
        };

        var dataCheckString = string.Join('\n', parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        var secret = HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"), Encoding.UTF8.GetBytes(botToken));
        var hash = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(dataCheckString));

        return string.Join('&', parameters.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))
            + "&hash="
            + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
