using System.Globalization;
using BotFramework.Host.Execution;
using Games.SecretHitler.Application.Execution;
using Microsoft.Extensions.Options;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Services;

/// <summary>Compatibility facade over the atomic Secret Hitler aggregate.</summary>
public sealed class SecretHitlerService(
    ISecretHitlerGameStore games,
    ISecretHitlerPlayerStore players,
    IAtomicGameExecutor<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult> createExecutor,
    IAtomicGameExecutor<ShJoinCommand, SecretHitlerExecutionState, ShJoinResult> joinExecutor,
    IGameStateExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult> startExecutor,
    IGameStateExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult> nominateExecutor,
    IAtomicGameExecutor<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult> voteExecutor,
    IGameStateExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult> discardExecutor,
    IAtomicGameExecutor<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult> enactExecutor,
    IAtomicGameExecutor<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult> leaveExecutor,
    IGameStateExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool> playerMessageExecutor,
    IGameStateExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool> publicMessageExecutor,
    IOptions<SecretHitlerOptions> options) : ISecretHitlerService
{
    private readonly SecretHitlerOptions settings = options.Value;

    public async Task<(ShGameSnapshot? Snapshot, SecretHitlerPlayer? Me)> FindMyGameAsync(
        long userId, CancellationToken ct)
    {
        var player = await players.FindByUserAsync(userId, ct);
        if (player is null) return (null, null);
        var game = await games.FindAsync(player.InviteCode, ct);
        if (game is null) return (null, null);
        var list = await players.ListByGameAsync(game.InviteCode, ct);
        return (new(game, list), list.First(item => item.UserId == userId));
    }

    public Task<ShCreateResult> CreateGameAsync(
        long userId, string displayName, long publicChatId, long playerChatId, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        return createExecutor.ExecuteAsync(new(new ShCreateCommand(userId, displayName,
            publicChatId, playerChatId, $"sh:create:{publicChatId}:{userId}:{id}", settings.BuyIn,
            [new(userId, playerChatId)])), ct);
    }

    public async Task<ShJoinResult> JoinGameAsync(
        long userId, string displayName, long playerChatId, string code, CancellationToken ct)
    {
        code = code.ToUpperInvariant();
        var game = await games.FindAsync(code, ct);
        var publicChatId = game?.ChatId ?? playerChatId;
        var expected = await ExpectedWalletsAsync(code, ct);
        expected.Add(new(userId, playerChatId));
        var revision = game?.LastActionAt ?? 0;
        return await joinExecutor.ExecuteAsync(new(new ShJoinCommand(code, userId, displayName,
            publicChatId, playerChatId, $"sh:join:{code}:{revision}:{userId}", settings.BuyIn, expected)), ct);
    }

    public async Task<ShStartResult> StartGameAsync(long userId, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return StartFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await startExecutor.ExecuteAsync(new(new ShStartCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:start:{game.InviteCode}:{game.LastActionAt}:{userId}", expected)), ct);
    }

    public async Task<ShNominateResult> NominateAsync(
        long userId, int chancellorPosition, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return NominateFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await nominateExecutor.ExecuteAsync(new(new ShNominateCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:nominate:{game.InviteCode}:{game.LastActionAt}:{userId}:{chancellorPosition}",
            chancellorPosition, expected)), ct);
    }

    public async Task<ShVoteResult> VoteAsync(long userId, ShVote vote, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return VoteFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await voteExecutor.ExecuteAsync(new(new ShVoteCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:vote:{game.InviteCode}:{game.LastActionAt}:{userId}:{vote}", vote, expected)), ct);
    }

    public async Task<ShDiscardResult> PresidentDiscardAsync(
        long userId, int discardIndex, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return DiscardFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await discardExecutor.ExecuteAsync(new(new ShDiscardCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:discard:{game.InviteCode}:{game.LastActionAt}:{userId}:{discardIndex}",
            discardIndex, expected)), ct);
    }

    public async Task<ShEnactResult> ChancellorEnactAsync(
        long userId, int enactIndex, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return EnactFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await enactExecutor.ExecuteAsync(new(new ShEnactCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:enact:{game.InviteCode}:{game.LastActionAt}:{userId}:{enactIndex}",
            enactIndex, expected)), ct);
    }

    public async Task<ShLeaveResult> LeaveAsync(long userId, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return LeaveFail(ShError.NotInGame);
        var (game, actor, expected) = loaded.Value;
        return await leaveExecutor.ExecuteAsync(new(new ShLeaveCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:leave:{game.InviteCode}:{userId}:{actor.JoinedAt}", expected)), ct);
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        var loaded = await LoadActorGameAsync(userId, ct);
        if (loaded is null) return;
        var (game, actor, expected) = loaded.Value;
        await playerMessageExecutor.ExecuteAsync(new(new ShPlayerMessageCommand(game.InviteCode, userId,
            actor.DisplayName, game.ChatId, actor.ChatId,
            $"sh:player-message:{game.InviteCode}:{userId}:{messageId}", messageId, expected)), ct);
    }

    public async Task SetPublicStateMessageIdAsync(string inviteCode, int messageId, CancellationToken ct)
    {
        var game = await games.FindAsync(inviteCode, ct);
        if (game is null) return;
        var expected = await ExpectedWalletsAsync(inviteCode, ct);
        await publicMessageExecutor.ExecuteAsync(new(new ShPublicMessageCommand(inviteCode,
            game.HostUserId, "sh", game.ChatId, game.ChatId,
            $"sh:public-message:{inviteCode}:{messageId}", messageId, expected)), ct);
    }

    private async Task<(SecretHitlerGame Game, SecretHitlerPlayer Actor,
        List<SecretHitlerWalletRef> Expected)?> LoadActorGameAsync(long userId, CancellationToken ct)
    {
        var actor = await players.FindByUserAsync(userId, ct);
        if (actor is null) return null;
        var game = await games.FindAsync(actor.InviteCode, ct);
        if (game is null) return null;
        return (game, actor, await ExpectedWalletsAsync(game.InviteCode, ct));
    }

    private async Task<List<SecretHitlerWalletRef>> ExpectedWalletsAsync(string code, CancellationToken ct) =>
        (await players.ListByGameAsync(code, ct))
        .Select(player => new SecretHitlerWalletRef(player.UserId, player.ChatId)).ToList();
}
