using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Football.Application.Wagering;

public sealed class FootballWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "football";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<FootballWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Football input is required.");
        if (input.Face is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(input.Face));
        return new("played", JsonSerializer.Serialize(new { face = input.Face }));
    }
}

public sealed record FootballWagerInput(int Face);
