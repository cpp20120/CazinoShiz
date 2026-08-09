using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Football.Application.Wagering;

public sealed class FootballWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "football";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<FootballEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Football outcome evidence is invalid.");
        return checked(terms.Stake * (evidence.Face is 4 or 5 ? 2 : 0));
    }

    private sealed record FootballEvidence(int Face);
}
