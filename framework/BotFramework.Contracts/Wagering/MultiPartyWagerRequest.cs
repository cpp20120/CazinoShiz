namespace BotFramework.Contracts.Wagering;

/// <summary>
/// Starts one game workflow backed by several independent wager reservations.
/// The game receives only opaque input and participant BetIds; it never
/// receives balances or a wallet transaction.
/// </summary>
public sealed record MultiPartyWagerRequest(
    string WorkflowId,
    string GameId,
    string GameInput,
    IReadOnlyList<MultiPartyWagerParticipant> Participants,
    string TenantId,
    string ScopeId,
    DateTimeOffset OccurredAt);
