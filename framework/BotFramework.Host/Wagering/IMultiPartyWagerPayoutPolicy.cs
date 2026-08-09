using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

/// <summary>
/// Wagering-side policy that turns game facts and frozen terms into payout
/// facts. Game adapters must not calculate or receive financial values.
/// </summary>
public interface IMultiPartyWagerPayoutPolicy
{
    string GameId { get; }

    IReadOnlyList<MultiPartyWagerOutcome> Calculate(
        MultiPartyWagerRequest request,
        IReadOnlyList<MultiPartyWagerGameOutcome> outcomes);
}
