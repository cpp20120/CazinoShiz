using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerSettlementFactory : IWagerSettlementCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "horse";

    public WagerSettlementPlan Create(WagerOperation operation, GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var evidence = JsonSerializer.Deserialize<SettlementEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Horse outcome evidence is invalid.");
        if (evidence.Payout < 0) throw new InvalidOperationException("Horse payout cannot be negative.");
        return new WagerSettlementPlan(evidence.Payout);
    }

    private sealed record SettlementEvidence(long Payout);
}
