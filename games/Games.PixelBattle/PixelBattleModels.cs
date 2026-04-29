using System.Text.Json.Serialization;

namespace Games.PixelBattle;

public sealed record PixelBattleGrid(string[] Tiles, string[] Versionstamps);

public sealed record PixelBattleUpdate(int Index, string Color, string Versionstamp);

public sealed record TelegramWebAppAuth(TelegramWebAppUser User, DateTimeOffset AuthDate);

public sealed record TelegramWebAppUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("username")] string? Username);
