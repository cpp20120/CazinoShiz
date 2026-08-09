using BotFramework.Host.Execution;
using Games.SecretHitler.Application.Execution;
using Games.SecretHitler.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class SecretHitlerApplicationServiceTests
{
    [Fact]
    public async Task FindMyGame_ReturnsSnapshotOnlyForKnownPlayerAndGame()
    {
        var fixture = CreateFixture();
        fixture.Players.Actor = Player("ABC", 42, 100);
        fixture.Games.ByCode = Game("ABC", 100);
        fixture.Players.Rows = [fixture.Players.Actor];

        var found = await fixture.Service.FindMyGameAsync(42, CancellationToken.None);

        Assert.NotNull(found.Snapshot);
        Assert.Same(fixture.Players.Actor, found.Me);
        Assert.Single(found.Snapshot!.Players);
    }

    [Fact]
    public async Task FindMyGame_WhenPlayerIsMissing_ReturnsEmpty()
    {
        var found = await CreateFixture().Service.FindMyGameAsync(42, CancellationToken.None);

        Assert.Null(found.Snapshot);
        Assert.Null(found.Me);
    }

    [Fact]
    public async Task CreateGame_MapsConfiguredBuyInAndActorWallet()
    {
        var fixture = CreateFixture(75);

        var result = await fixture.Service.CreateGameAsync(42, "Alice", 100, 200, CancellationToken.None);
        var command = Assert.IsType<ShCreateCommand>(fixture.Create.Last!.Command);

        Assert.Equal(ShError.None, result.Error);
        Assert.Equal(75, command.BuyIn);
        Assert.Equal(100, command.PublicChatId);
        Assert.Equal(200, command.ActorChatId);
        Assert.Equal(new SecretHitlerWalletRef(42, 200), Assert.Single(command.ExpectedWallets));
    }

    [Fact]
    public async Task Join_UppercasesCodeAndUsesExistingGameChatAndWallets()
    {
        var fixture = CreateFixture();
        fixture.Games.ByCode = Game("ABC", 999);
        fixture.Players.Rows = [Player("ABC", 7, 700)];

        var result = await fixture.Service.JoinGameAsync(42, "Alice", 200, "abc", CancellationToken.None);
        var command = Assert.IsType<ShJoinCommand>(fixture.Join.Last!.Command);

        Assert.Equal(ShError.None, result.Error);
        Assert.Equal("ABC", command.InviteCode);
        Assert.Equal(999, command.PublicChatId);
        Assert.Equal(2, command.ExpectedWallets.Count);
        Assert.Contains(new SecretHitlerWalletRef(7, 700), command.ExpectedWallets);
        Assert.Contains(new SecretHitlerWalletRef(42, 200), command.ExpectedWallets);
    }

    [Fact]
    public async Task ActorCommands_WhenPlayerOrGameMissing_ReturnNotInGame()
    {
        var fixture = CreateFixture();

        var start = await fixture.Service.StartGameAsync(42, CancellationToken.None);
        var nominate = await fixture.Service.NominateAsync(42, 2, CancellationToken.None);
        var vote = await fixture.Service.VoteAsync(42, ShVote.Ja, CancellationToken.None);
        var discard = await fixture.Service.PresidentDiscardAsync(42, 0, CancellationToken.None);
        var enact = await fixture.Service.ChancellorEnactAsync(42, 0, CancellationToken.None);
        var leave = await fixture.Service.LeaveAsync(42, CancellationToken.None);

        Assert.Equal(ShError.NotInGame, start.Error);
        Assert.Equal(ShError.NotInGame, nominate.Error);
        Assert.Equal(ShError.NotInGame, vote.Error);
        Assert.Equal(ShError.NotInGame, discard.Error);
        Assert.Equal(ShError.NotInGame, enact.Error);
        Assert.Equal(ShError.NotInGame, leave.Error);
        Assert.Null(fixture.Start.Last);
        Assert.Null(fixture.Vote.Last);
        Assert.Null(fixture.Leave.Last);
    }

    [Fact]
    public async Task ActorCommands_UseLoadedGameAndExpectedWallets()
    {
        var fixture = CreateFixture();
        fixture.Games.ByCode = Game("ABC", 100);
        fixture.Players.Actor = Player("ABC", 42, 200);
        fixture.Players.Rows = [fixture.Players.Actor, Player("ABC", 7, 700)];

        await fixture.Service.StartGameAsync(42, CancellationToken.None);
        await fixture.Service.NominateAsync(42, 2, CancellationToken.None);
        await fixture.Service.VoteAsync(42, ShVote.Nein, CancellationToken.None);
        await fixture.Service.PresidentDiscardAsync(42, 1, CancellationToken.None);
        await fixture.Service.ChancellorEnactAsync(42, 0, CancellationToken.None);
        await fixture.Service.LeaveAsync(42, CancellationToken.None);

        var start = Assert.IsType<ShStartCommand>(fixture.Start.Last!.Command);
        var vote = Assert.IsType<ShVoteCommand>(fixture.Vote.Last!.Command);
        var leave = Assert.IsType<ShLeaveCommand>(fixture.Leave.Last!.Command);
        Assert.Equal("ABC", start.Code);
        Assert.Equal(ShVote.Nein, vote.Vote);
        Assert.Equal(2, leave.Wallets.Count);
        Assert.Equal(2, Assert.IsType<ShNominateCommand>(fixture.Nominate.Last!.Command).ChancellorPosition);
        Assert.Equal(1, Assert.IsType<ShDiscardCommand>(fixture.Discard.Last!.Command).DiscardIndex);
        Assert.Equal(0, Assert.IsType<ShEnactCommand>(fixture.Enact.Last!.Command).EnactIndex);
    }

    [Fact]
    public async Task MessageIds_AreForwardedOnlyWhenAggregateExists()
    {
        var fixture = CreateFixture();
        await fixture.Service.SetStateMessageIdAsync(42, 55, CancellationToken.None);
        await fixture.Service.SetPublicStateMessageIdAsync("ABC", 56, CancellationToken.None);
        Assert.Null(fixture.PlayerMessage.Last);
        Assert.Null(fixture.PublicMessage.Last);

        fixture.Games.ByCode = Game("ABC", 100);
        fixture.Players.Actor = Player("ABC", 42, 200);
        fixture.Players.Rows = [fixture.Players.Actor];
        await fixture.Service.SetStateMessageIdAsync(42, 55, CancellationToken.None);
        await fixture.Service.SetPublicStateMessageIdAsync("ABC", 56, CancellationToken.None);

        Assert.Equal(55, Assert.IsType<ShPlayerMessageCommand>(fixture.PlayerMessage.Last!.Command).MessageId);
        Assert.Equal(56, Assert.IsType<ShPublicMessageCommand>(fixture.PublicMessage.Last!.Command).MessageId);
    }

    private static Fixture CreateFixture(int buyIn = 50)
    {
        var games = new GameStoreStub();
        var players = new PlayerStoreStub();
        return new Fixture(
            games,
            players,
            new RecordingExecutor<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult>(new(ShError.None, "ABC", buyIn)),
            new RecordingExecutor<ShJoinCommand, SecretHitlerExecutionState, ShJoinResult>(new(ShError.None, null, 2, 7)),
            new RecordingExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult>(new(ShError.None, null)),
            new RecordingExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult>(new(ShError.None, null)),
            new RecordingExecutor<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult>(new(ShError.None, null, null)),
            new RecordingExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult>(new(ShError.None, null)),
            new RecordingExecutor<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult>(new(ShError.None, null, null)),
            new RecordingExecutor<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult>(new(ShError.None, null, false)),
            new RecordingExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool>(true),
            new RecordingExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool>(true),
            Options.Create(new SecretHitlerOptions { BuyIn = buyIn }));
    }

    private static SecretHitlerGame Game(string code, long chatId) => new()
    {
        InviteCode = code,
        ChatId = chatId,
        HostUserId = 42,
        LastActionAt = 123,
    };

    private static SecretHitlerPlayer Player(string code, long userId, long chatId) => new()
    {
        InviteCode = code,
        UserId = userId,
        ChatId = chatId,
        Position = userId == 42 ? 1 : 2,
        DisplayName = $"user-{userId}",
        JoinedAt = 10,
    };

    private sealed class Fixture
    {
        public Fixture(
            GameStoreStub games,
            PlayerStoreStub players,
            RecordingExecutor<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult> create,
            RecordingExecutor<ShJoinCommand, SecretHitlerExecutionState, ShJoinResult> join,
            RecordingExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult> start,
            RecordingExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult> nominate,
            RecordingExecutor<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult> vote,
            RecordingExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult> discard,
            RecordingExecutor<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult> enact,
            RecordingExecutor<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult> leave,
            RecordingExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool> playerMessage,
            RecordingExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool> publicMessage,
            IOptions<SecretHitlerOptions> options)
        {
            Games = games;
            Players = players;
            Create = create;
            Join = join;
            Start = start;
            Nominate = nominate;
            Vote = vote;
            Discard = discard;
            Enact = enact;
            Leave = leave;
            PlayerMessage = playerMessage;
            PublicMessage = publicMessage;
            Service = new SecretHitlerService(games, players, create, join, start, nominate, vote, discard, enact, leave, playerMessage, publicMessage, options);
        }

        public SecretHitlerService Service { get; }
        public GameStoreStub Games { get; }
        public PlayerStoreStub Players { get; }
        public RecordingExecutor<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult> Create { get; }
        public RecordingExecutor<ShJoinCommand, SecretHitlerExecutionState, ShJoinResult> Join { get; }
        public RecordingExecutor<ShStartCommand, SecretHitlerExecutionState, ShStartResult> Start { get; }
        public RecordingExecutor<ShNominateCommand, SecretHitlerExecutionState, ShNominateResult> Nominate { get; }
        public RecordingExecutor<ShVoteCommand, SecretHitlerExecutionState, ShVoteResult> Vote { get; }
        public RecordingExecutor<ShDiscardCommand, SecretHitlerExecutionState, ShDiscardResult> Discard { get; }
        public RecordingExecutor<ShEnactCommand, SecretHitlerExecutionState, ShEnactResult> Enact { get; }
        public RecordingExecutor<ShLeaveCommand, SecretHitlerExecutionState, ShLeaveResult> Leave { get; }
        public RecordingExecutor<ShPlayerMessageCommand, SecretHitlerExecutionState, bool> PlayerMessage { get; }
        public RecordingExecutor<ShPublicMessageCommand, SecretHitlerExecutionState, bool> PublicMessage { get; }
    }

    private sealed class GameStoreStub : ISecretHitlerGameStore
    {
        public SecretHitlerGame? ByCode { get; set; }
        public Task<SecretHitlerGame?> FindAsync(string inviteCode, CancellationToken ct) => Task.FromResult(ByCode);
        public Task<SecretHitlerGame?> FindOpenByChatAsync(long chatId, CancellationToken ct) => Task.FromResult(ByCode);
        public Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct) => Task.FromResult(false);
        public Task InsertAsync(SecretHitlerGame game, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(SecretHitlerGame game, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertStateMessageAsync(string inviteCode, int messageId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class PlayerStoreStub : ISecretHitlerPlayerStore
    {
        public SecretHitlerPlayer? Actor { get; set; }
        public IReadOnlyList<SecretHitlerPlayer> Rows { get; set; } = [];
        public Task<SecretHitlerPlayer?> FindByUserAsync(long userId, CancellationToken ct) => Task.FromResult(Actor?.UserId == userId ? Actor : null);
        public Task<IReadOnlyList<SecretHitlerPlayer>> ListByGameAsync(string inviteCode, CancellationToken ct) => Task.FromResult(Rows);
        public Task<bool> AnyForUserAsync(long userId, CancellationToken ct) => Task.FromResult(Actor?.UserId == userId);
        public Task<int> CountByGameAsync(string inviteCode, CancellationToken ct) => Task.FromResult(Rows.Count);
        public Task InsertAsync(SecretHitlerPlayer player, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(SecretHitlerPlayer player, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(string inviteCode, int position, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertStateMessageAsync(long userId, int messageId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingExecutor<TCommand, TState, TResult>(TResult result)
        : IAtomicGameExecutor<TCommand, TState, TResult>,
            IGameStateExecutor<TCommand, TState, TResult>
    {
        public Type StateType => typeof(TState);
        public GameExecutionEnvelope<TCommand>? Last { get; private set; }
        public TResult Result { get; set; } = result;

        public Task<TResult> ExecuteAsync(GameExecutionEnvelope<TCommand> envelope, CancellationToken ct)
        {
            Last = envelope;
            return Task.FromResult(Result);
        }
    }
}
