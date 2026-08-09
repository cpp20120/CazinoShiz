using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Contracts.Domain.Results;

public sealed record BlackjackWagerResult(
    BlackjackError Error,
    BlackjackWagerSnapshot? Snapshot,
    int? StateMessageId = null);
