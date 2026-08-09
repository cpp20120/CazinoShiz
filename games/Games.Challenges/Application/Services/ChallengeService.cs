using BotFramework.Host.Execution;
using Games.Challenges.Application.Execution;
using Microsoft.Extensions.Options;

namespace Games.Challenges.Application.Services;

public sealed class ChallengeService(
    IChallengeStore store,
    IAtomicGameExecutor<ChallengeCreateCommand, ChallengeExecutionState, ChallengeCreateResult> createExecutor,
    IAtomicGameExecutor<ChallengeAcceptCommand, ChallengeExecutionState, ChallengeAcceptResult> acceptExecutor,
    IGameStateExecutor<ChallengeDeclineCommand, ChallengeExecutionState, ChallengeAcceptError> declineExecutor,
    IAtomicGameExecutor<ChallengeCompleteCommand, ChallengeExecutionState, ChallengeAcceptResult> completeExecutor,
    IAtomicGameExecutor<ChallengeFailCommand, ChallengeExecutionState, bool> failExecutor,
    IOptions<ChallengeOptions> options) : IChallengeService
{
    public Task<ChallengeUser?> FindKnownUserByUsernameAsync(long chatId, string username, CancellationToken ct) =>
        store.FindKnownUserByUsernameAsync(chatId, username, ct);

    public Task<ChallengeCreateResult> CreateAsync(
        long challengerId, string challengerName, ChallengeUser target, long chatId,
        int amount, ChallengeGame game, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var opts = options.Value;
        return createExecutor.ExecuteAsync(new(new ChallengeCreateCommand(
            id, challengerId, challengerName, target, chatId, amount, game,
            opts.MinBet, opts.MaxBet, opts.PendingTtl, $"challenge:create:{id:N}",
            [new(challengerId, chatId)])), ct);
    }

    public async Task<ChallengeAcceptResult> BeginAcceptAsync(
        Guid challengeId, long actorId, CancellationToken ct)
    {
        var challenge = await store.FindAsync(challengeId, ct);
        if (challenge is null) return new(ChallengeAcceptError.NotFound);
        return await acceptExecutor.ExecuteAsync(new(new ChallengeAcceptCommand(
            challengeId, actorId, challenge.TargetName, challenge.ChatId,
            $"challenge:accept:{challengeId:N}", Wallets(challenge))), ct);
    }

    public async Task<ChallengeAcceptError> DeclineAsync(
        Guid challengeId, long actorId, CancellationToken ct)
    {
        var challenge = await store.FindAsync(challengeId, ct);
        if (challenge is null) return ChallengeAcceptError.NotFound;
        return await declineExecutor.ExecuteAsync(new(new ChallengeDeclineCommand(
            challengeId, actorId, challenge.TargetName, challenge.ChatId,
            $"challenge:decline:{challengeId:N}", [])), ct);
    }

    public Task<ChallengeAcceptResult> CompleteAcceptedAsync(
        Challenge challenge, int challengerRoll, int targetRoll, CancellationToken ct) =>
        completeExecutor.ExecuteAsync(new(new ChallengeCompleteCommand(
            challenge.Id, challenge.ChallengerId, challenge.ChallengerName, challenge.ChatId,
            challengerRoll, targetRoll, options.Value.HouseFeeBasisPoints,
            $"challenge:complete:{challenge.Id:N}", Wallets(challenge))), ct);

    public async Task FailAcceptedAsync(Challenge challenge, CancellationToken ct) =>
        _ = await failExecutor.ExecuteAsync(new(new ChallengeFailCommand(
            challenge.Id, challenge.ChallengerId, challenge.ChallengerName, challenge.ChatId,
            $"challenge:fail:{challenge.Id:N}", Wallets(challenge))), ct);

    private static IReadOnlyList<ChallengeWalletRef> Wallets(Challenge challenge) =>
        [new(challenge.ChallengerId, challenge.ChatId), new(challenge.TargetId, challenge.ChatId)];
}
