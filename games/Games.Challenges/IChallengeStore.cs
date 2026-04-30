namespace Games.Challenges;

public interface IChallengeStore
{
    Task<ChallengeUser?> FindKnownUserByUsernameAsync(long chatId, string username, CancellationToken ct);
    Task<bool> HasPendingAsync(long challengerId, long targetId, long chatId, CancellationToken ct);
    Task InsertAsync(Challenge challenge, CancellationToken ct);
    Task<Challenge?> FindAsync(Guid id, CancellationToken ct);
    Task<bool> TryMarkStatusAsync(Guid id, ChallengeStatus expected, ChallengeStatus next, CancellationToken ct);
}
