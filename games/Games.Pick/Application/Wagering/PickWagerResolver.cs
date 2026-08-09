using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Pick.Application.Wagering;

public sealed class PickWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "pick";

    public WagerGameResolution Resolve(WagerGameCommand command, WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas, EntropyValue entropy, DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<PickWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Pick input is required.");
        if (input.Variants is null || input.BackedIndices is null
            || input.Variants.Count is < 2 or > 64
            || input.BackedIndices.Count == 0
            || input.BackedIndices.Count >= input.Variants.Count
            || input.BackedIndices.Any(index => index < 0 || index >= input.Variants.Count))
            throw new ArgumentOutOfRangeException(nameof(input.BackedIndices));
        var houseEdge = Math.Clamp(input.HouseEdge, 0, 1);
        var picked = Math.Min(input.Variants.Count - 1,
            (int)(entropy.GetDouble("outcome") * input.Variants.Count));
        var won = input.BackedIndices.Contains(picked);
        var multiplier = won
            ? input.Variants.Count / (double)input.BackedIndices.Count * (1 - houseEdge)
            : 0;
        return new("played", JsonSerializer.Serialize(new PickEvidence(
            picked, won, multiplier, input.Variants.Count, input.BackedIndices.Count)));
    }

    private sealed record PickEvidence(int PickedIndex, bool Won, double Multiplier,
        int VariantCount, int BackedCount);
}

public sealed record PickWagerInput(
    IReadOnlyList<string> Variants,
    IReadOnlyList<int> BackedIndices,
    double HouseEdge = 0.03);
