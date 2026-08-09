using BotFramework.Contracts.Messaging;
using BotFramework.Contracts.Wagering;

namespace Games.Blackjack.Application.Wagering;

/// <summary>
/// Wagering-side payout adapter. It is the only Blackjack component that reads
/// stake or payout rules; the game aggregate never receives either value.
/// </summary>
public sealed class BlackjackWagerSettlementFactory : IWagerSettlementCommandFactory
{
    public string GameId => "blackjack";

    public IIntegrationCommand Create(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms) =>
        new LedgerSettlementRequested(
            operation.OperationId,
            operation.BetId,
            operation.PlayerId,
            BlackjackWagerPayoutPolicy.Calculate(outcome, terms),
            terms.Currency,
            outcome.OccurredAt);
}
