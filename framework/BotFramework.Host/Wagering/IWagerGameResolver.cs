using BotFramework.Contracts.Wagering;
using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Wagering;

public interface IWagerGameResolver
{
    string GameId { get; }

    WagerGameResolution Resolve(
        WagerGameCommand command,
        WagerGameState state,
        IReadOnlyDictionary<string, QuotaSnapshot> quotas,
        EntropyValue entropy,
        DateTimeOffset utcNow);
}
