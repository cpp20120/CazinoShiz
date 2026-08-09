using System.Text.Json;
using BotFramework.Contracts.Wagering;
using BotFramework.Host.Wagering;

namespace Games.Pick.Application.Wagering;

public sealed class PickWagerPayoutPolicy : IWagerPayoutPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "pick";

    public long CalculatePayout(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (outcome.OutcomeCode == "invalid_input") return terms.Stake;
        if (outcome.OutcomeCode != "played") return 0;
        var evidence = JsonSerializer.Deserialize<PickEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Pick outcome evidence is invalid.");
        if (!evidence.Won || double.IsNaN(evidence.Multiplier) || double.IsInfinity(evidence.Multiplier)
            || evidence.Multiplier < 0)
            return 0;
        return checked((long)Math.Floor(terms.Stake * evidence.Multiplier));
    }

    private sealed record PickEvidence(int PickedIndex, bool Won, double Multiplier,
        int VariantCount, int BackedCount);
}
