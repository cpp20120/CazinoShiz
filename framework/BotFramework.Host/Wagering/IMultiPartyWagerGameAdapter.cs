using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

/// <summary>
/// Game-side boundary for a wager with several participants. Implementations
/// return game facts only; the coordinator owns all money transitions.
/// </summary>
public interface IMultiPartyWagerGameAdapter
{
    string GameId { get; }

    Task<IReadOnlyList<MultiPartyWagerGameOutcome>> ExecuteAsync(
        MultiPartyWagerGameRequest request,
        CancellationToken ct);
}
