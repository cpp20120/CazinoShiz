using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Bowling.Application.Wagering;

public sealed class BowlingWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "bowling";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<BowlingWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Bowling input is required.");
        if (input.Face is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(input.Face));
        return new("played", JsonSerializer.Serialize(new { face = input.Face }));
    }
}

public sealed record BowlingWagerInput(int Face);
