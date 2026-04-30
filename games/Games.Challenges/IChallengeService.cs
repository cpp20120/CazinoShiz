namespace Games.Challenges;

public interface IChallengeService
{
    Task<ChallengeUser?> FindKnownUserByUsernameAsync(long chatId, string username, CancellationToken ct);

    Task<ChallengeCreateResult> CreateAsync(
        long challengerId,
        string challengerName,
        ChallengeUser target,
        long chatId,
        int amount,
        ChallengeGame game,
        CancellationToken ct);

    Task<ChallengeAcceptResult> BeginAcceptAsync(Guid challengeId, long actorId, CancellationToken ct);

    Task<ChallengeAcceptError> DeclineAsync(Guid challengeId, long actorId, CancellationToken ct);

    Task<ChallengeAcceptResult> CompleteAcceptedAsync(
        Challenge challenge,
        int challengerRoll,
        int targetRoll,
        CancellationToken ct);

    Task FailAcceptedAsync(Challenge challenge, CancellationToken ct);
}
