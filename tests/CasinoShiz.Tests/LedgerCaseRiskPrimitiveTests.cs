using BotFramework.Contracts.Cases;
using BotFramework.Contracts.Ledger;
using BotFramework.Contracts.Risk;
using Xunit;

namespace CasinoShiz.Tests;

public sealed class LedgerCaseRiskPrimitiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CaseLifecycleSupportsEvidenceReviewResolutionAndAppeal()
    {
        var opened = CaseLifecycle.Open("case-1", "payment_dispute", "player-1", "system", "duplicate charge", Now);
        Assert.True(opened.Accepted);
        Assert.Equal(CaseStatus.Open, opened.State!.Status);

        var evidence = CaseLifecycle.AddEvidence(
            opened.State,
            new CaseEvidence("evidence-1", "receipt", "s3://evidence/1", "player-1", Now));
        Assert.True(evidence.Accepted);
        Assert.Equal(CaseStatus.Evidence, evidence.State!.Status);

        var review = CaseLifecycle.StartReview(evidence.State, "operator-1");
        Assert.True(review.Accepted);
        Assert.Equal(CaseStatus.Review, review.State!.Status);

        var resolved = CaseLifecycle.Resolve(review.State, "operator-1", "refund", "approved", Now.AddMinutes(5));
        Assert.True(resolved.Accepted);
        Assert.Equal(CaseStatus.Resolved, resolved.State!.Status);

        var appeal = CaseLifecycle.Appeal(resolved.State, "new bank statement", Now.AddMinutes(10));
        Assert.True(appeal.Accepted);
        Assert.Equal(CaseStatus.Appealed, appeal.State!.Status);
        Assert.Equal(1, appeal.State.Evidence.Count);
    }

    [Fact]
    public void CaseLifecycleRejectsInvalidHumanTransitions()
    {
        var opened = CaseLifecycle.Open("case-1", "fraud", "player-1", "risk", "velocity", Now).State!;

        var resolved = CaseLifecycle.Resolve(opened, "operator-1", "deny", null, Now);
        var appeal = CaseLifecycle.Appeal(opened, "more evidence", Now);

        Assert.False(resolved.Accepted);
        Assert.Equal("case_not_in_review", resolved.ErrorCode);
        Assert.False(appeal.Accepted);
        Assert.Equal("case_not_appealable", appeal.ErrorCode);
    }

    [Fact]
    public void RiskDecisionsArePolicyOutputAndCarryWorkflowReferences()
    {
        var allow = RiskDecision.Allow("risk.v1");
        var deny = RiskDecision.Deny("risk.v1", "blocked_country");
        var review = RiskDecision.Review("risk.v2", "manual_review", "case-1");
        var hold = RiskDecision.Hold("risk.v2", "velocity", "hold-1", Now.AddMinutes(15));

        Assert.Equal(RiskDecisionKind.Allow, allow.Kind);
        Assert.Equal("blocked_country", deny.ReasonCode);
        Assert.Equal("case-1", review.CaseId);
        Assert.Equal("hold-1", hold.HoldId);
        Assert.Equal(Now.AddMinutes(15), hold.ExpiresAt);
    }

    [Fact]
    public void LedgerCommandsHaveStableOperationTypesAndSeparateCaptureFromPayout()
    {
        var hold = new LedgerHoldRequested("op-hold", "hold-1", "player-1", 100, "coins", Now.AddMinutes(5), "wager", Now);
        var capture = new LedgerCaptureRequested("op-capture", "hold-1", "player-1", 100, "coins", "wager", Now);
        var payout = new LedgerTransferRequested("op-payout", "house", "player-1", 200, "coins", "wager.payout", Now);

        Assert.Equal("ledger.hold.requested", hold.CommandType);
        Assert.Equal("ledger.capture.requested", capture.CommandType);
        Assert.Equal("ledger.transfer.requested", payout.CommandType);
        Assert.NotEqual(capture.OperationId, payout.OperationId);
        Assert.Equal("ledger:player-1", hold.MessageKey);
    }
}
