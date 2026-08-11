using System.Text.Json;
using BotFramework.Contracts.Wagering;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerSettlementFactory : IWagerSettlementCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "poker";

    public WagerSettlementPlan Create(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var evidence = JsonSerializer.Deserialize<PokerSettlementEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Poker outcome evidence is invalid.");
        if (evidence.Payout < 0)
            throw new InvalidOperationException("Poker payout cannot be negative.");
        return new WagerSettlementPlan(evidence.Payout);
    }

    private sealed record PokerSettlementEvidence(long Payout);
}
