using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Wagering-owned payout policy adapter; Game modules never implement it.</summary>
public interface IWagerSettlementCommandFactory
{
    string GameId { get; }
    IIntegrationCommand Create(WagerOperation operation, GameOutcomeDeclared outcome, WagerTermsSnapshot terms);
}
