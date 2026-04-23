namespace Games.Darts;

public interface IDartsRoundStore
{
    Task<long> InsertQueuedAsync(DartsRound row, CancellationToken ct);
    Task<DartsRound?> FindByIdAsync(long roundId, CancellationToken ct);
    Task<IReadOnlyList<DartsRound>> ListQueuedAsync(CancellationToken ct);
    Task<bool> TryMarkAwaitingOutcomeAsync(long roundId, int botMessageId, CancellationToken ct);
    Task DeleteAsync(long roundId, CancellationToken ct);
    /// <summary>Rounds in this chat still in flight that finish before <paramref name="roundId"/>.</summary>
    Task<int> CountRollsAheadInChatAsync(long chatId, long roundId, CancellationToken ct);
    /// <summary>Queued or awaiting bot outcome for this player in this chat.</summary>
    Task<int> CountActiveByUserChatAsync(long userId, long chatId, CancellationToken ct);
}
