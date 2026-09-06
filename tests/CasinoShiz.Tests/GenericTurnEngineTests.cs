using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GenericTurnEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartAndAdvance_RotatePlayersAndRoundsInDeclaredOrder()
    {
        var engine = new TurnEngine<string>();
        var waiting = engine.CreateWaiting(["alice", "bob", "carol"]);

        var started = engine.Start(waiting, Now.AddMinutes(1));
        var afterAlice = engine.Advance(started.State, started.State.CurrentTurn, Now.AddMinutes(2));
        var afterBob = engine.Advance(afterAlice.State, afterAlice.State.CurrentTurn, Now.AddMinutes(3));
        var afterCarol = engine.Advance(afterBob.State, afterBob.State.CurrentTurn, Now.AddMinutes(4));

        Assert.True(started.Applied);
        Assert.Equal(("alice", 1L, 1L), (started.State.CurrentPlayerId, started.State.TurnNumber, started.State.Round));
        Assert.Equal(("bob", 2L, 1L), (afterAlice.State.CurrentPlayerId, afterAlice.State.TurnNumber, afterAlice.State.Round));
        Assert.Equal(("carol", 3L, 1L), (afterBob.State.CurrentPlayerId, afterBob.State.TurnNumber, afterBob.State.Round));
        Assert.Equal(("alice", 4L, 2L), (afterCarol.State.CurrentPlayerId, afterCarol.State.TurnNumber, afterCarol.State.Round));
    }

    [Fact]
    public void Timeout_RejectsEarlyAndStaleDeliveryWithoutChangingTheNewTurn()
    {
        var engine = new TurnEngine<long>();
        var started = engine.Start(engine.CreateWaiting([10L, 20L]), Now.AddMinutes(1));
        var firstTurn = started.State.CurrentTurn;

        var early = engine.Timeout(started.State, firstTurn, Now, Now.AddMinutes(2));
        var advanced = engine.Timeout(started.State, firstTurn, Now.AddMinutes(1), Now.AddMinutes(2));
        var stale = engine.Timeout(advanced.State, firstTurn, Now.AddMinutes(3), Now.AddMinutes(4));

        Assert.False(early.Applied);
        Assert.Equal("turn_not_expired", early.Rejection!.Code);
        Assert.Same(started.State, early.State);
        Assert.True(advanced.Applied);
        Assert.Equal(20L, advanced.State.CurrentPlayerId);
        Assert.False(stale.Applied);
        Assert.Equal("stale_turn", stale.Rejection!.Code);
        Assert.Same(advanced.State, stale.State);
    }

    [Fact]
    public void SuspendResumeAndLeave_PreserveOrAdvanceTheCorrectTurn()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob"]), Now.AddMinutes(1));
        var token = started.State.CurrentTurn;

        var suspended = engine.Suspend(started.State, token);
        var resumed = engine.Resume(suspended.State, token, Now.AddMinutes(5));
        var afterLeave = engine.RemovePlayer(resumed.State, "alice", Now.AddMinutes(6));

        Assert.True(suspended.Applied);
        Assert.Equal(TurnGameStatus.Suspended, suspended.State.Status);
        Assert.Null(suspended.State.TurnDeadline);
        Assert.True(resumed.Applied);
        Assert.Equal(token, resumed.State.CurrentTurn);
        Assert.Equal(Now.AddMinutes(5), resumed.State.TurnDeadline);
        Assert.True(afterLeave.Applied);
        Assert.Equal("bob", afterLeave.State.CurrentPlayerId);
        Assert.Equal(2, afterLeave.State.TurnNumber);
        Assert.Equal(1, afterLeave.State.Round);
        Assert.Equal(Now.AddMinutes(6), afterLeave.State.TurnDeadline);
    }

    [Fact]
    public void PlayerMembership_IsCopiedAndJoinLeaveAreOnlyAllowedBeforeStart()
    {
        var supplied = new[] { "alice" };
        var engine = new TurnEngine<string>();
        var waiting = engine.CreateWaiting(supplied);
        supplied[0] = "mutated";

        var joined = engine.AddPlayer(waiting, "bob");
        var started = engine.Start(joined.State);
        var lateJoin = engine.AddPlayer(started.State, "carol");

        Assert.Equal("alice", waiting.PlayerOrder[0]);
        Assert.Equal(["alice", "bob"], joined.State.PlayerOrder);
        Assert.False(lateJoin.Applied);
        Assert.Equal("game_not_waiting", lateJoin.Rejection!.Code);
    }

    [Fact]
    public void Apply_PassesDomainStateEffectsAndAnExplicitNextPlayerTogether()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob", "carol"]), Now.AddMinutes(1));
        var effects = new GameEffectSet([], [QuotaEffect.Consume("turn")], [], [], [], []);
        var result = new TurnResult<int, string>(
            42,
            effects,
            TurnCompletion.PassTo("carol", Now.AddMinutes(2)));

        var applied = engine.Apply(41, started.State, started.State.CurrentTurn, result);

        Assert.True(applied.Applied);
        Assert.Equal(42, applied.State);
        Assert.Same(effects, applied.Effects);
        Assert.Equal(TurnCompletionKind.PassTo, applied.Completion!.Kind);
        Assert.Equal("carol", applied.Turns.CurrentPlayerId);
        Assert.Equal(2, applied.Turns.TurnNumber);
        Assert.Equal(1, applied.Turns.Round);
        Assert.Equal(Now.AddMinutes(2), applied.Turns.TurnDeadline);

        var decision = applied.ToGameDecision("passed", rejection => rejection.Code);
        Assert.Equal(DecisionStatus.Accepted, decision.Status);
        Assert.Equal("passed", decision.Result);
        Assert.Equal(effects.Quotas, decision.Quotas);
    }

    [Fact]
    public void Apply_WaitFor_EmitsAReusableInputEffectAndUsesItsExpiryAsDeadline()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob"]), Now.AddMinutes(1));
        var input = new InputRequestEffect(
            "dice:choose:1",
            "alice",
            "table:1",
            "dice.choose",
            Now.AddMinutes(3),
            allowedValues: ["one", "two"],
            sessionId: "table:1");
        var result = new TurnResult<string, string>(
            "waiting-for-choice",
            GameEffectSet.Empty,
            TurnCompletion.WaitFor<string>(input));

        var applied = engine.Apply("before-choice", started.State, started.State.CurrentTurn, result);

        Assert.True(applied.Applied);
        Assert.Equal("waiting-for-choice", applied.State);
        Assert.Equal("alice", applied.Turns.CurrentPlayerId);
        Assert.Equal(1, applied.Turns.TurnNumber);
        Assert.Equal(Now.AddMinutes(3), applied.Turns.TurnDeadline);
        var emitted = Assert.IsType<InputRequestEffect>(Assert.Single(applied.Effects.Custom));
        Assert.Same(input, emitted);
        Assert.Equal(TurnCompletionKind.WaitForInput, applied.Completion!.Kind);
    }

    [Fact]
    public void Apply_StaleTokenDropsNewStateAndEffects()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob"]));
        var firstTurn = started.State.CurrentTurn;
        var advanced = engine.Advance(started.State, firstTurn);
        var result = new TurnResult<int, string>(
            42,
            new GameEffectSet([], [QuotaEffect.Consume("turn")], [], [], [], []),
            TurnCompletion.Complete<string>());

        var rejected = engine.Apply(7, advanced.State, firstTurn, result);

        Assert.False(rejected.Applied);
        Assert.Equal(7, rejected.State);
        Assert.Same(advanced.State, rejected.Turns);
        Assert.Equal(0, rejected.Effects.Count);
        Assert.Null(rejected.Completion);
        Assert.Equal("stale_turn", rejected.Rejection!.Code);

        var decision = rejected.ToGameDecision("ignored", rejection => rejection.Code);
        Assert.Equal(DecisionStatus.Rejected, decision.Status);
        Assert.Equal("stale_turn", decision.Result);
        Assert.Empty(decision.Quotas);
    }

    [Fact]
    public void Apply_CompleteClearsTheCurrentTurn()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob"]));
        var result = new TurnResult<string, string>(
            "finished",
            GameEffectSet.Empty,
            TurnCompletion.Complete<string>());

        var applied = engine.Apply("playing", started.State, started.State.CurrentTurn, result);

        Assert.True(applied.Applied);
        Assert.Equal("finished", applied.State);
        Assert.Equal(TurnGameStatus.Completed, applied.Turns.Status);
        Assert.False(applied.Turns.HasCurrentTurn);
    }

    [Fact]
    public void Apply_WithTurnStateBinder_ReturnsOneReadyToPersistAggregate()
    {
        var engine = new TurnEngine<string>();
        var started = engine.Start(engine.CreateWaiting(["alice", "bob"]));
        var current = new BoundGameState(7, started.State);
        var result = new TurnResult<BoundGameState, string>(
            current with { Revision = 8 },
            GameEffectSet.Empty,
            TurnCompletion.PassTo("bob"));

        var applied = engine.Apply(
            current,
            current.Turns,
            current.Turns.CurrentTurn,
            result,
            static (state, turns) => state with { Turns = turns });

        Assert.True(applied.Applied);
        Assert.Equal(8, applied.State.Revision);
        Assert.Same(applied.Turns, applied.State.Turns);
        Assert.Equal("bob", applied.State.Turns.CurrentPlayerId);
    }

    private sealed record BoundGameState(long Revision, TurnEngineState<string> Turns);
}
