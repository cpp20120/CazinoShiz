using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Horse.Application.Wagering;

public sealed class HorseWagerSettlementFactory : IWagerSettlementCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "horse";

    public IIntegrationCommand Create(WagerOperation operation, GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var evidence = JsonSerializer.Deserialize<SettlementEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Horse outcome evidence is invalid.");
        if (evidence.Payout < 0) throw new InvalidOperationException("Horse payout cannot be negative.");
        return new LedgerSettlementRequested(operation.OperationId, operation.BetId,
            operation.PlayerId, evidence.Payout, terms.Currency, outcome.OccurredAt);
    }

    private sealed record SettlementEvidence(long Payout);
}
