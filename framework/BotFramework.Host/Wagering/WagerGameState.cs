using BotFramework.Sdk.Execution;

namespace BotFramework.Host.Wagering;

/// <summary>
/// Generic state for one-shot wager games. It contains no financial data.
/// </summary>
public sealed record WagerGameState(
    long Revision,
    string GameId,
    string BetId,
    string PlayerId,
    string? OutcomeCode,
    string? Evidence) : IVersionedGameState;
