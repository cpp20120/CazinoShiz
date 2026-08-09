using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Bowling.Application.Wagering;

public sealed class BowlingWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "bowling";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<BowlingEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Bowling outcome evidence is invalid.");
        var multiplier = evidence.Face switch { 4 => 1, 5 or 6 => 2, _ => 0 };
        return checked(terms.Stake * multiplier);
    }

    private sealed record BowlingEvidence(int Face);
}
