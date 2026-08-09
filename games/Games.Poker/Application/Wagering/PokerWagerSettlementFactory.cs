using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Poker.Application.Wagering;

public sealed class PokerWagerSettlementFactory : IWagerSettlementCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string GameId => "poker";

    public IIntegrationCommand Create(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var evidence = JsonSerializer.Deserialize<PokerSettlementEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Poker outcome evidence is invalid.");
        if (evidence.Payout < 0)
            throw new InvalidOperationException("Poker payout cannot be negative.");
        return new LedgerSettlementRequested(
            operation.OperationId,
            operation.BetId,
            operation.PlayerId,
            evidence.Payout,
            terms.Currency,
            outcome.OccurredAt);
    }

    private sealed record PokerSettlementEvidence(long Payout);
}
