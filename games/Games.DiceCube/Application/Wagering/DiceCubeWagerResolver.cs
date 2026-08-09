using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.DiceCube.Application.Wagering;

public sealed class DiceCubeWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "dicecube";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<DiceCubeWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("DiceCube input is required.");
        if (input.Face is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(input.Face));
        return new("played", JsonSerializer.Serialize(new { face = input.Face }));
    }
}

public sealed record DiceCubeWagerInput(int Face);
