using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Darts.Application.Wagering;

public sealed class DartsWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "darts";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<DartsWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Darts input is required.");
        if (input.Face is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(input.Face));
        return new("played", JsonSerializer.Serialize(new { face = input.Face }));
    }
}

public sealed record DartsWagerInput(int Face);
