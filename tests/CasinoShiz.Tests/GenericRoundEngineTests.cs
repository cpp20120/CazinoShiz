using BotFramework.Sdk.Execution;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class GenericRoundEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartAdvanceAndNextRound_MaintainMonotonicPhaseTokens()
    {
        var engine = new RoundEngine<Phase>();
        var waiting = engine.CreateWaiting(Phase.Setup);

        var started = engine.Start(waiting, Now.AddMinutes(1));
        var play = engine.AdvanceTo(
            started.State,
            started.State.CurrentPhase,
            Phase.Play,
            Now.AddMinutes(2));
        var nextRound = engine.NextRound(
            play.State,
            play.State.CurrentPhase,
            Phase.Setup,
            Now.AddMinutes(3));

        Assert.True(started.Applied);
        Assert.Equal((1L, 1L, Phase.Setup), Token(started.State));
        Assert.Equal((1L, 2L, Phase.Play), Token(play.State));
        Assert.Equal((2L, 3L, Phase.Setup), Token(nextRound.State));
        Assert.Equal(Now.AddMinutes(3), nextRound.State.PhaseDeadline);
    }

    [Fact]
    public void Apply_WaitFor_EmitsInputAndKeepsTheExactPhase()
    {
        var engine = new RoundEngine<Phase>();
        var started = engine.Start(engine.CreateWaiting(Phase.Play), Now.AddMinutes(1));
        var input = new InputRequestEffect(
            "quiz:answer:1",
            "alice",
            "quiz:1",
            "quiz.answer",
            Now.AddMinutes(4),
            allowedValues: ["a", "b"],
            sessionId: "quiz:1");
        var result = new RoundResult<string, Phase>(
            "waiting-for-answer",
            GameEffectSet.Empty,
            RoundCompletion.WaitFor<Phase>(input));

        var applied = engine.Apply("before-answer", started.State, started.State.CurrentPhase, result);

        Assert.True(applied.Applied);
        Assert.Equal("waiting-for-answer", applied.State);
        Assert.Equal((1L, 1L, Phase.Play), Token(applied.Rounds));
        Assert.Equal(Now.AddMinutes(4), applied.Rounds.PhaseDeadline);
        Assert.Same(input, Assert.IsType<InputRequestEffect>(Assert.Single(applied.Effects.Custom)));
        Assert.Equal(RoundCompletionKind.WaitForInput, applied.Completion!.Kind);
    }

    [Fact]
    public void Apply_StalePhaseDropsNewAggregateAndEffects()
    {
        var engine = new RoundEngine<Phase>();
        var started = engine.Start(engine.CreateWaiting(Phase.Setup));
        var stale = started.State.CurrentPhase;
        var playing = engine.AdvanceTo(started.State, stale, Phase.Play);
        var result = new RoundResult<int, Phase>(
            42,
            new GameEffectSet([], [QuotaEffect.Consume("round")], [], [], [], []),
            RoundCompletion.Complete<Phase>());

        var rejected = engine.Apply(7, playing.State, stale, result);

        Assert.False(rejected.Applied);
        Assert.Equal(7, rejected.State);
        Assert.Same(playing.State, rejected.Rounds);
        Assert.Equal(0, rejected.Effects.Count);
        Assert.Null(rejected.Completion);
        Assert.Equal("stale_phase", rejected.Rejection!.Code);

        var decision = rejected.ToGameDecision("ignored", rejection => rejection.Code);
        Assert.Equal(DecisionStatus.Rejected, decision.Status);
        Assert.Equal("stale_phase", decision.Result);
        Assert.Empty(decision.Quotas);
    }

    [Fact]
    public void ApplyTimeout_RejectsEarlyAndOnlyBindsTheAggregateAfterTheDeadline()
    {
        var engine = new RoundEngine<Phase>();
        var started = engine.Start(engine.CreateWaiting(Phase.Vote), Now.AddMinutes(2));
        var current = new MatchState(5, started.State);
        var result = new RoundResult<MatchState, Phase>(
            current with { Revision = 6 },
            GameEffectSet.Empty,
            RoundCompletion.AdvanceTo(Phase.Resolve, Now.AddMinutes(3)));

        var early = engine.ApplyTimeout(
            current,
            current.Rounds,
            current.Rounds.CurrentPhase,
            Now.AddMinutes(1),
            result,
            static (state, rounds) => state with { Rounds = rounds });
        var applied = engine.ApplyTimeout(
            current,
            current.Rounds,
            current.Rounds.CurrentPhase,
            Now.AddMinutes(2),
            result,
            static (state, rounds) => state with { Rounds = rounds });

        Assert.False(early.Applied);
        Assert.Equal("phase_not_expired", early.Rejection!.Code);
        Assert.Same(current, early.State);
        Assert.True(applied.Applied);
        Assert.Equal(6, applied.State.Revision);
        Assert.Same(applied.Rounds, applied.State.Rounds);
        Assert.Equal(Phase.Resolve, applied.State.Rounds.Phase);
        Assert.Equal((1L, 2L, Phase.Resolve), Token(applied.State.Rounds));
    }

    [Fact]
    public void DeadlineSchedule_IsDerivedFromTokenAndRoundTripsTheTypedCommand()
    {
        var phase = new RoundPhaseToken<Phase>(3, 9, Phase.Vote);
        var command = new PhaseDeadlineCommand(phase);

        var effect = RoundDeadlineSchedule.Schedule(Now.AddMinutes(5), command);
        var restored = AtomicGameSchedule.DeserializeCommand<PhaseDeadlineCommand>(effect.Data!);

        Assert.Equal(ScheduleEffectKind.Schedule, effect.Kind);
        Assert.Equal("round-phase:3:9", effect.ScheduleId);
        Assert.Equal(AtomicGameSchedule.JobKey<PhaseDeadlineCommand>(), effect.JobKey);
        Assert.Equal(command, restored);
        Assert.Equal(
            RoundDeadlineSchedule.Cancel(phase).ScheduleId,
            effect.ScheduleId);

        var replacement = RoundDeadlineSchedule.Replace(phase, Now.AddMinutes(6), command);
        Assert.Collection(
            replacement,
            cancel => Assert.Equal(ScheduleEffectKind.Cancel, cancel.Kind),
            schedule => Assert.Equal(ScheduleEffectKind.Schedule, schedule.Kind));
    }

    [Fact]
    public void SuspendResumeAndAbortWaiting_ClearDeadlineAndRemainValid()
    {
        var engine = new RoundEngine<Phase>();
        var waiting = engine.CreateWaiting(Phase.Setup);
        var aborted = engine.Abort(waiting);
        var started = engine.Start(engine.CreateWaiting(Phase.Setup), Now.AddMinutes(1));
        var suspended = engine.Suspend(started.State, started.State.CurrentPhase);
        var resumed = engine.Resume(suspended.State, suspended.State.CurrentPhase, Now.AddMinutes(3));

        Assert.True(aborted.Applied);
        Assert.Equal(RoundGameStatus.Aborted, aborted.State.Status);
        Assert.Equal(0, aborted.State.Round);
        Assert.True(suspended.Applied);
        Assert.Equal(RoundGameStatus.Suspended, suspended.State.Status);
        Assert.Null(suspended.State.PhaseDeadline);
        Assert.True(resumed.Applied);
        Assert.Equal(Now.AddMinutes(3), resumed.State.PhaseDeadline);
    }

    private static (long Round, long PhaseNumber, Phase Phase) Token(RoundEngineState<Phase> state) =>
        (state.CurrentPhase.Round, state.CurrentPhase.PhaseNumber, state.CurrentPhase.Phase);

    private enum Phase
    {
        Setup,
        Play,
        Vote,
        Resolve,
    }

    private sealed record MatchState(long Revision, RoundEngineState<Phase> Rounds);

    private sealed record PhaseDeadlineCommand(RoundPhaseToken<Phase> ExpectedPhase)
        : IRoundDeadlineCommand<Phase>;
}
