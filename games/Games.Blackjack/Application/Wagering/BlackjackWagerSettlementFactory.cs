using BotFramework.Contracts.Wagering;

namespace Games.Blackjack.Application.Wagering;

/// <summary>
/// Wagering-side payout adapter. It is the only Blackjack component that reads
/// stake or payout rules; the game aggregate never receives either value.
/// </summary>
public sealed class BlackjackWagerSettlementFactory : IWagerSettlementCommandFactory
{
    public string GameId => "blackjack";

    public WagerSettlementPlan Create(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms) =>
        new(BlackjackWagerPayoutPolicy.Calculate(outcome, terms));
}
