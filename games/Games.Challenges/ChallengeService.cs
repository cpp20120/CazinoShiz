using BotFramework.Host;
using Microsoft.Extensions.Options;

namespace Games.Challenges;

public sealed class ChallengeService(
    IChallengeStore store,
    IEconomicsService economics,
    IAnalyticsService analytics,
    IOptions<ChallengeOptions> options,
    TimeProvider? timeProvider = null) : IChallengeService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<ChallengeUser?> FindKnownUserByUsernameAsync(long chatId, string username, CancellationToken ct) =>
        store.FindKnownUserByUsernameAsync(chatId, username, ct);

    public async Task<ChallengeCreateResult> CreateAsync(
        long challengerId,
        string challengerName,
        ChallengeUser target,
        long chatId,
        int amount,
        ChallengeGame game,
        CancellationToken ct)
    {
        var opts = options.Value;
        if (target.UserId == challengerId)
            return new ChallengeCreateResult(ChallengeCreateError.SelfChallenge);
        if (amount < opts.MinBet || amount > opts.MaxBet)
            return new ChallengeCreateResult(ChallengeCreateError.InvalidAmount);

        await economics.EnsureUserAsync(challengerId, chatId, challengerName, ct);
        var balance = await economics.GetBalanceAsync(challengerId, chatId, ct);
        if (balance < amount)
            return new ChallengeCreateResult(ChallengeCreateError.NotEnoughCoins, Balance: balance);

        if (await store.HasPendingAsync(challengerId, target.UserId, chatId, ct))
            return new ChallengeCreateResult(ChallengeCreateError.AlreadyPending);

        var now = _timeProvider.GetUtcNow();
        var challenge = new Challenge(
            Guid.NewGuid(),
            chatId,
            challengerId,
            challengerName,
            target.UserId,
            target.DisplayName,
            amount,
            game,
            ChallengeStatus.Pending,
            now,
            now.Add(opts.PendingTtl));

        await store.InsertAsync(challenge, ct);
        analytics.Track("challenges", "created", Tags(challenge));
        return new ChallengeCreateResult(ChallengeCreateError.None, challenge);
    }

    public async Task<ChallengeAcceptResult> BeginAcceptAsync(Guid challengeId, long actorId, CancellationToken ct)
    {
        var challenge = await store.FindAsync(challengeId, ct);
        if (challenge is null)
            return new ChallengeAcceptResult(ChallengeAcceptError.NotFound);
        if (challenge.Status != ChallengeStatus.Pending)
            return new ChallengeAcceptResult(ChallengeAcceptError.AlreadyResolved, challenge);
        if (challenge.TargetId != actorId)
            return new ChallengeAcceptResult(ChallengeAcceptError.NotTarget, challenge);
        if (challenge.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Pending, ChallengeStatus.Failed, ct);
            return new ChallengeAcceptResult(ChallengeAcceptError.Expired, challenge);
        }
        if (!await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Pending, ChallengeStatus.Accepted, ct))
            return new ChallengeAcceptResult(ChallengeAcceptError.AlreadyResolved, challenge);

        await economics.EnsureUserAsync(challenge.ChallengerId, challenge.ChatId, challenge.ChallengerName, ct);
        await economics.EnsureUserAsync(challenge.TargetId, challenge.ChatId, challenge.TargetName, ct);

        if (!await economics.TryDebitAsync(
                challenge.ChallengerId,
                challenge.ChatId,
                challenge.Amount,
                "challenge.stake",
                ct))
        {
            await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Accepted, ChallengeStatus.Failed, ct);
            return new ChallengeAcceptResult(ChallengeAcceptError.ChallengerNotEnoughCoins, challenge);
        }

        if (!await economics.TryDebitAsync(
                challenge.TargetId,
                challenge.ChatId,
                challenge.Amount,
                "challenge.stake",
                ct))
        {
            await economics.CreditAsync(
                challenge.ChallengerId,
                challenge.ChatId,
                challenge.Amount,
                "challenge.refund",
                ct);
            await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Accepted, ChallengeStatus.Failed, ct);
            return new ChallengeAcceptResult(ChallengeAcceptError.TargetNotEnoughCoins, challenge);
        }

        analytics.Track("challenges", "accepted", Tags(challenge));
        return new ChallengeAcceptResult(ChallengeAcceptError.None, challenge);
    }

    public async Task<ChallengeAcceptError> DeclineAsync(Guid challengeId, long actorId, CancellationToken ct)
    {
        var challenge = await store.FindAsync(challengeId, ct);
        if (challenge is null)
            return ChallengeAcceptError.NotFound;
        if (challenge.TargetId != actorId)
            return ChallengeAcceptError.NotTarget;
        if (challenge.Status != ChallengeStatus.Pending)
            return ChallengeAcceptError.AlreadyResolved;

        if (await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Pending, ChallengeStatus.Declined, ct))
        {
            analytics.Track("challenges", "declined", Tags(challenge));
            return ChallengeAcceptError.None;
        }

        return ChallengeAcceptError.AlreadyResolved;
    }

    public async Task<ChallengeAcceptResult> CompleteAcceptedAsync(
        Challenge challenge,
        int challengerRoll,
        int targetRoll,
        CancellationToken ct)
    {
        if (challengerRoll == targetRoll)
        {
            await economics.CreditAsync(challenge.ChallengerId, challenge.ChatId, challenge.Amount, "challenge.tie_refund", ct);
            await economics.CreditAsync(challenge.TargetId, challenge.ChatId, challenge.Amount, "challenge.tie_refund", ct);
            await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Accepted, ChallengeStatus.Completed, ct);
            analytics.Track("challenges", "tie", Tags(challenge, challengerRoll, targetRoll));
            return new ChallengeAcceptResult(
                ChallengeAcceptError.None,
                challenge,
                challengerRoll,
                targetRoll,
                IsTie: true);
        }

        var winnerId = challengerRoll > targetRoll ? challenge.ChallengerId : challenge.TargetId;
        var winnerName = challengerRoll > targetRoll ? challenge.ChallengerName : challenge.TargetName;
        var pot = challenge.Amount * 2;
        var fee = Math.Clamp(options.Value.HouseFeeBasisPoints, 0, 10_000) * pot / 10_000;
        var payout = pot - fee;

        await economics.CreditAsync(winnerId, challenge.ChatId, payout, "challenge.payout", ct);
        await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Accepted, ChallengeStatus.Completed, ct);
        analytics.Track("challenges", "completed", Tags(challenge, challengerRoll, targetRoll, winnerId, payout, fee));

        return new ChallengeAcceptResult(
            ChallengeAcceptError.None,
            challenge,
            challengerRoll,
            targetRoll,
            winnerId,
            winnerName,
            payout,
            fee);
    }

    public async Task FailAcceptedAsync(Challenge challenge, CancellationToken ct)
    {
        await economics.CreditAsync(challenge.ChallengerId, challenge.ChatId, challenge.Amount, "challenge.refund", ct);
        await economics.CreditAsync(challenge.TargetId, challenge.ChatId, challenge.Amount, "challenge.refund", ct);
        await store.TryMarkStatusAsync(challenge.Id, ChallengeStatus.Accepted, ChallengeStatus.Failed, ct);
        analytics.Track("challenges", "failed_refunded", Tags(challenge));
    }

    private static Dictionary<string, object?> Tags(Challenge challenge) => new()
    {
        ["challenge_id"] = challenge.Id,
        ["chat_id"] = challenge.ChatId,
        ["challenger_id"] = challenge.ChallengerId,
        ["target_id"] = challenge.TargetId,
        ["amount"] = challenge.Amount,
        ["game"] = ChallengeGameCatalog.DisplayName(challenge.Game),
    };

    private static Dictionary<string, object?> Tags(
        Challenge challenge,
        int challengerRoll,
        int targetRoll,
        long winnerId = 0,
        int payout = 0,
        int fee = 0)
    {
        var tags = Tags(challenge);
        tags["challenger_roll"] = challengerRoll;
        tags["target_roll"] = targetRoll;
        tags["winner_id"] = winnerId;
        tags["payout"] = payout;
        tags["fee"] = fee;
        return tags;
    }
}
