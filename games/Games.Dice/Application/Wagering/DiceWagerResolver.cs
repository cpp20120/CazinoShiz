using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;
using BotFramework.Sdk.Execution;

namespace Games.Dice.Application.Wagering;

public sealed class DiceWagerResolver : IWagerGameResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "dice";

    public WagerGameResolution Resolve(
        WagerGameCommand command,
        WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas,
        EntropyValue entropy,
        DateTimeOffset utcNow)
    {
        var input = JsonSerializer.Deserialize<DiceWagerInput>(command.GameInput, JsonOptions)
            ?? throw new JsonException("Dice input is required.");
        if (input.DiceValue is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(input.DiceValue));
        return new("played", JsonSerializer.Serialize(new { diceValue = input.DiceValue }));
    }

    internal static int Prize(int value)
    {
        var rolls = new[] { (value - 1) & 0b11, ((value - 1) >> 2) & 0b11, ((value - 1) >> 4) & 0b11 };
        var frequencies = new Dictionary<int, int>();
        foreach (var roll in rolls) frequencies[roll] = frequencies.GetValueOrDefault(roll) + 1;
        var maxFrequency = frequencies.Values.Max();
        var maxFrequent = frequencies.First(pair => pair.Value == maxFrequency).Key;
        var rollSum = rolls.Sum(item => new[] { 1, 1, 2, 3 }[item]);
        return (maxFrequent, maxFrequency) switch
        {
            (3, 3) => 77,
            (2, 3) => 30,
            (1, 3) => 23,
            (0, 3) => 21,
            (3, 2) => 10 + rollSum,
            (2, 2) => 6 + rollSum,
            (_, 2) => 4 + rollSum,
            _ => rollSum - 3,
        };
    }
}

public sealed record DiceWagerInput(int DiceValue);
