using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Dice.Application.Wagering;

public sealed class DiceWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "dice";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<DiceEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Dice outcome evidence is invalid.");
        return DiceWagerResolver.Prize(evidence.DiceValue);
    }

    private sealed record DiceEvidence(int DiceValue);
}
