using BotFramework.Contracts.Messaging;

namespace BotFramework.Contracts.Wagering;

/// <summary>Game-owned adapter that turns an accepted wager into an outcome-only command.</summary>
public interface IWagerGameCommandFactory
{
    string GameId { get; }
    IIntegrationCommand Create(WagerRequested wager);
}
