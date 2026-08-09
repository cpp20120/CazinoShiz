using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.DiceCube.Application.Wagering;

public sealed class DiceCubeWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "dicecube";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<DiceCubeEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("DiceCube outcome evidence is invalid.");
        var multiplier = evidence.Face switch { 4 => 1, 5 or 6 => 2, _ => 0 };
        return checked(terms.Stake * multiplier);
    }

    private sealed record DiceCubeEvidence(int Face);
}
