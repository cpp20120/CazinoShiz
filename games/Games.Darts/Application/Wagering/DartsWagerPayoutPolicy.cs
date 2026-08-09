using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Darts.Application.Wagering;

public sealed class DartsWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "darts";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<DartsEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Darts outcome evidence is invalid.");
        return checked(terms.Stake * (evidence.Face switch { 4 => 1, 5 or 6 => 2, _ => 0 }));
    }

    private sealed record DartsEvidence(int Face);
}
