using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Basketball.Application.Wagering;

public sealed class BasketballWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "basketball";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<BasketballWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Basketball input is required.");
        if (input.Face is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(input.Face));
        return new("played", JsonSerializer.Serialize(new { face = input.Face }));
    }
}

public sealed record BasketballWagerInput(int Face);
