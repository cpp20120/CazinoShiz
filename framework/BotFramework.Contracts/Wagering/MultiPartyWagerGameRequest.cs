namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Game-only projection of a multi-party wager. Financial terms stay in the
/// Wagering-owned request and are never sent to the game adapter.
/// </summary>
public sealed record MultiPartyWagerGameRequest(
    string WorkflowId,
    string GameId,
    string GameInput,
    IReadOnlyList<MultiPartyWagerGameParticipant> Participants);
