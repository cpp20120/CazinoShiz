using BotFramework.Host.Execution;
using Games.Poker.Application.Execution;
using Games.Poker.Infrastructure.Persistence;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class PokerApplicationServiceTests
{
    [Fact]
    public async Task FindMyTable_ReturnsSnapshotOnlyForSeatedUser()
    {
        var fixture = CreateFixture();
        fixture.Tables.Open = Table("ABC", 100);
        fixture.Seats.Rows = [Seat("ABC", 42, 1, 100)];

        var found = await fixture.Service.FindMyTableAsync(42, 100, CancellationToken.None);

        Assert.NotNull(found.Snapshot);
        Assert.Same(fixture.Seats.Rows[0], found.MySeat);
        Assert.Single(found.Snapshot!.Seats);
    }

    [Fact]
    public async Task FindMyTable_WhenUserHasNoOpenTable_ReturnsEmpty()
    {
        var fixture = CreateFixture();

        var found = await fixture.Service.FindMyTableAsync(42, 100, CancellationToken.None);

        Assert.Null(found.Snapshot);
        Assert.Null(found.MySeat);
    }

    [Fact]
    public async Task CreateTable_MapsRuntimeOptionsToAtomicCommand()
    {
        var fixture = CreateFixture(new PokerOptions
        {
            BuyIn = 77,
            SmallBlind = 3,
            BigBlind = 6,
            MaxPlayers = 5,
        });

        var result = await fixture.Service.CreateTableAsync(42, "Alice", 100, 9, CancellationToken.None);
        var command = Assert.IsType<PokerCreateCommand>(fixture.Create.Last!.Command);

        Assert.Equal(PokerError.None, result.Error);
        Assert.Equal(77, command.BuyIn);
        Assert.Equal(3, command.SmallBlind);
        Assert.Equal(6, command.BigBlind);
        Assert.Equal(new PokerWalletRef(42, 100), Assert.Single(command.ExpectedWallets));
        Assert.Contains(":9:", command.CommandId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Join_UppercasesCodeAndIncludesExistingWallets()
    {
        var fixture = CreateFixture();
        fixture.Seats.Rows = [Seat("ABC", 7, 1, 200)];

        var result = await fixture.Service.JoinTableAsync(42, "Alice", 100, "abc", 11, CancellationToken.None);
        var command = Assert.IsType<PokerJoinCommand>(fixture.Join.Last!.Command);

        Assert.Equal(PokerError.None, result.Error);
        Assert.Equal("ABC", command.InviteCode);
        Assert.Equal(2, command.ExpectedWallets.Count);
        Assert.Contains(new PokerWalletRef(7, 200), command.ExpectedWallets);
        Assert.Contains(new PokerWalletRef(42, 100), command.ExpectedWallets);
    }

    [Fact]
    public async Task JoinCurrent_WhenChatHasNoTable_ReturnsNoTableWithoutExecuting()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.JoinTableAsync(42, "Alice", 100, "", CancellationToken.None);

        Assert.Equal(PokerError.NoTable, result.Error);
        Assert.Null(fixture.Join.Last);
    }

    [Fact]
    public async Task StartTurnAndLeave_WhenChatHasNoTable_ReturnGuardResults()
    {
        var fixture = CreateFixture();

        var start = await fixture.Service.StartHandAsync(42, 100, CancellationToken.None);
        var turn = await fixture.Service.ApplyPlayerActionAsync(42, 100, "check", 0, CancellationToken.None);
        var leave = await fixture.Service.LeaveTableAsync(42, 100, CancellationToken.None);

        Assert.Equal(PokerError.NoTable, start.Error);
        Assert.Equal(PokerError.NoTable, turn.Error);
        Assert.Equal(PokerError.NoTable, leave.Error);
        Assert.Null(fixture.Start.Last);
        Assert.Null(fixture.Turn.Last);
        Assert.Null(fixture.Leave.Last);
    }

    [Fact]
    public async Task RunAutoAction_OnlyExecutesForCurrentSeatedPlayer()
    {
        var fixture = CreateFixture();
        fixture.Tables.ByCode = Table("ABC", 100);
        fixture.Seats.Rows = [Seat("ABC", 42, 1, 100)];

        var inactive = await fixture.Service.RunAutoActionAsync("ABC", CancellationToken.None);
        Assert.Null(inactive);

        fixture.Tables.ByCode!.Status = PokerTableStatus.HandActive;
        fixture.Tables.ByCode.CurrentSeat = 1;
        fixture.Auto.Result = new ActionResult(PokerError.NotYourTurn, null, default, null, null, null);
        var stale = await fixture.Service.RunAutoActionAsync("ABC", CancellationToken.None);
        Assert.Null(stale);

        fixture.Auto.Result = new ActionResult(PokerError.None, null, default, null, null, null);
        var executed = await fixture.Service.RunAutoActionAsync("ABC", CancellationToken.None);
        Assert.NotNull(executed);
        Assert.Equal(42, Assert.IsType<PokerAutoTurnCommand>(fixture.Auto.Last!.Command).ActorUserId);
    }

    [Fact]
    public async Task MessageIdAndStuckCodes_AreForwardedToStoresAndExecutors()
    {
        var fixture = CreateFixture();
        fixture.Tables.ByCode = Table("ABC", 100);
        fixture.Tables.Stuck = ["ABC", "XYZ"];

        await fixture.Service.SetTableStateMessageIdAsync("ABC", 55, CancellationToken.None);
        var command = Assert.IsType<PokerSetMessageCommand>(fixture.Message.Last!.Command);
        var stuck = await fixture.Service.ListStuckCodesAsync(123, CancellationToken.None);

        Assert.Equal(55, command.MessageId);
        Assert.Equal(["ABC", "XYZ"], stuck);
    }

    private static Fixture CreateFixture(PokerOptions? options = null)
    {
        var tuning = new FakeRuntimeTuning { Poker = options ?? new PokerOptions() };
        return new Fixture(
            new TableStoreStub(),
            new SeatStoreStub(),
            new RecordingExecutor<PokerCreateCommand, PokerExecutionState, CreateResult>(new(PokerError.None, "ABC", 100)),
            new RecordingExecutor<PokerJoinCommand, PokerExecutionState, JoinResult>(new(PokerError.None, null, 2, 8)),
            new RecordingExecutor<PokerStartCommand, PokerExecutionState, StartResult>(new(PokerError.None, null)),
            new RecordingExecutor<PokerPlayerTurnCommand, PokerExecutionState, ActionResult>(new(PokerError.None, null, default, null, null, null)),
            new RecordingExecutor<PokerAutoTurnCommand, PokerExecutionState, ActionResult>(new(PokerError.None, null, default, null, null, null)),
            new RecordingExecutor<PokerLeaveCommand, PokerExecutionState, LeaveResult>(new(PokerError.None, null, false)),
            new RecordingExecutor<PokerSetMessageCommand, PokerExecutionState, bool>(true),
            tuning);
    }

    private static PokerTable Table(string code, long chatId) => new()
    {
        InviteCode = code,
        ChatId = chatId,
        HostUserId = 42,
        Status = PokerTableStatus.Seating,
        LastActionAt = 123,
    };

    private static PokerSeat Seat(string code, long userId, int position, long chatId) => new()
    {
        InviteCode = code,
        UserId = userId,
        Position = position,
        ChatId = chatId,
        DisplayName = $"user-{userId}",
        JoinedAt = 10,
    };

    private sealed class Fixture
    {
        public Fixture(
            TableStoreStub tables,
            SeatStoreStub seats,
            RecordingExecutor<PokerCreateCommand, PokerExecutionState, CreateResult> create,
            RecordingExecutor<PokerJoinCommand, PokerExecutionState, JoinResult> join,
            RecordingExecutor<PokerStartCommand, PokerExecutionState, StartResult> start,
            RecordingExecutor<PokerPlayerTurnCommand, PokerExecutionState, ActionResult> turn,
            RecordingExecutor<PokerAutoTurnCommand, PokerExecutionState, ActionResult> auto,
            RecordingExecutor<PokerLeaveCommand, PokerExecutionState, LeaveResult> leave,
            RecordingExecutor<PokerSetMessageCommand, PokerExecutionState, bool> message,
            IRuntimeTuningAccessor tuning)
        {
            Tables = tables;
            Seats = seats;
            Create = create;
            Join = join;
            Start = start;
            Turn = turn;
            Auto = auto;
            Leave = leave;
            Message = message;
            Service = new PokerService(tables, seats, create, join, start, turn, auto, leave, message, tuning);
        }

        public PokerService Service { get; }
        public TableStoreStub Tables { get; }
        public SeatStoreStub Seats { get; }
        public RecordingExecutor<PokerCreateCommand, PokerExecutionState, CreateResult> Create { get; }
        public RecordingExecutor<PokerJoinCommand, PokerExecutionState, JoinResult> Join { get; }
        public RecordingExecutor<PokerStartCommand, PokerExecutionState, StartResult> Start { get; }
        public RecordingExecutor<PokerPlayerTurnCommand, PokerExecutionState, ActionResult> Turn { get; }
        public RecordingExecutor<PokerAutoTurnCommand, PokerExecutionState, ActionResult> Auto { get; }
        public RecordingExecutor<PokerLeaveCommand, PokerExecutionState, LeaveResult> Leave { get; }
        public RecordingExecutor<PokerSetMessageCommand, PokerExecutionState, bool> Message { get; }
    }

    private sealed class TableStoreStub : IPokerTableStore
    {
        public PokerTable? Open { get; set; }
        public PokerTable? ByCode { get; set; }
        public IReadOnlyList<string> Stuck { get; set; } = [];

        public Task<PokerTable?> FindAsync(string inviteCode, CancellationToken ct) => Task.FromResult(ByCode);
        public Task<PokerTable?> FindOpenByChatAsync(long chatId, CancellationToken ct) => Task.FromResult(Open);
        public Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct) => Task.FromResult(false);
        public Task InsertAsync(PokerTable table, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(PokerTable table, CancellationToken ct) => Task.CompletedTask;
        public Task UpsertStateMessageAsync(string inviteCode, int messageId, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct) => Task.FromResult(Stuck);
    }

    private sealed class SeatStoreStub : IPokerSeatStore
    {
        public IReadOnlyList<PokerSeat> Rows { get; set; } = [];

        public Task<PokerSeat?> FindByUserAsync(long userId, CancellationToken ct) => Task.FromResult(Rows.FirstOrDefault(x => x.UserId == userId));
        public Task<PokerSeat?> FindByUserInTableAsync(long userId, string inviteCode, CancellationToken ct) =>
            Task.FromResult(Rows.FirstOrDefault(x => x.UserId == userId && x.InviteCode == inviteCode));
        public Task<IReadOnlyList<PokerSeat>> ListByTableAsync(string inviteCode, CancellationToken ct) => Task.FromResult(Rows);
        public Task<int> CountByTableAsync(string inviteCode, long exceptUserId, CancellationToken ct) => Task.FromResult(Rows.Count);
        public Task<bool> AnyForUserAsync(long userId, CancellationToken ct) => Task.FromResult(Rows.Any(x => x.UserId == userId));
        public Task InsertAsync(PokerSeat seat, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(PokerSeat seat, CancellationToken ct) => Task.CompletedTask;
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
