namespace BotFramework.Contracts.Wagering;

/// <summary>Wagering-owned payout policy adapter; Game modules never implement it.</summary>
public interface IWagerSettlementCommandFactory
{
    string GameId { get; }
    WagerSettlementPlan Create(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms);
}
