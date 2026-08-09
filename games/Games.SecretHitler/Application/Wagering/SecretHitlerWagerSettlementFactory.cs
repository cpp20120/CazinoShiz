using System.Text.Json;
using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerSettlementFactory : IWagerSettlementCommandFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string GameId => "sh";

    public IIntegrationCommand Create(WagerOperation operation, GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var evidence = JsonSerializer.Deserialize<SettlementEvidence>(outcome.Evidence, JsonOptions)
            ?? throw new InvalidOperationException("Secret Hitler outcome evidence is invalid.");
        if (evidence.Payout < 0) throw new InvalidOperationException("Secret Hitler payout cannot be negative.");
        return new LedgerSettlementRequested(operation.OperationId, operation.BetId,
            operation.PlayerId, evidence.Payout, terms.Currency, outcome.OccurredAt);
    }

    private sealed record SettlementEvidence(long Payout);
}
