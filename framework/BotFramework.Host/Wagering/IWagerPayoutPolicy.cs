using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

public interface IWagerPayoutPolicy
{
    string GameId { get; }

    long CalculatePayout(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms);
}
