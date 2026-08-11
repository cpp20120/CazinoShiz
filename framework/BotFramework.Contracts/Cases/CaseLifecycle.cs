namespace BotFramework.Contracts.Cases;

/// <summary>
/// Small deterministic case state machine reusable by disputes, fraud review,
/// moderation and other human-in-the-loop workflows.
/// </summary>
public static class CaseLifecycle
{
    public static CaseTransitionResult Open(
        string caseId,
        string caseType,
        string subjectId,
        string openedBy,
        string reason,
        DateTimeOffset openedAt)
    {
        if (string.IsNullOrWhiteSpace(caseId)) return CaseTransitionResult.Reject("case_id_required");
        if (string.IsNullOrWhiteSpace(caseType)) return CaseTransitionResult.Reject("case_type_required");
        if (string.IsNullOrWhiteSpace(subjectId)) return CaseTransitionResult.Reject("subject_id_required");
        if (string.IsNullOrWhiteSpace(openedBy)) return CaseTransitionResult.Reject("opened_by_required");
        if (string.IsNullOrWhiteSpace(reason)) return CaseTransitionResult.Reject("reason_required");

        return CaseTransitionResult.Accept(new CaseState(
            caseId, caseType, subjectId, openedBy, reason, CaseStatus.Open, openedAt, []));
    }

    public static CaseTransitionResult AddEvidence(
        CaseState state,
        CaseEvidence evidence)
    {
        if (state.Status is CaseStatus.Resolved or CaseStatus.Appealed)
            return CaseTransitionResult.Reject("case_not_accepting_evidence");
        if (state.Evidence.Any(item => string.Equals(item.EvidenceId, evidence.EvidenceId, StringComparison.Ordinal)))
            return CaseTransitionResult.Accept(state);

        return CaseTransitionResult.Accept(state with
        {
            Status = CaseStatus.Evidence,
            Evidence = [.. state.Evidence, evidence],
            Version = state.Version + 1,
        });
    }

    public static CaseTransitionResult StartReview(CaseState state, string reviewerId)
    {
        if (state.Status is not (CaseStatus.Open or CaseStatus.Evidence))
            return CaseTransitionResult.Reject("case_not_reviewable");
        if (string.IsNullOrWhiteSpace(reviewerId))
            return CaseTransitionResult.Reject("reviewer_required");

        return CaseTransitionResult.Accept(state with
        {
            Status = CaseStatus.Review,
            ReviewerId = reviewerId,
            Version = state.Version + 1,
        });
    }

    public static CaseTransitionResult Resolve(
        CaseState state,
        string resolverId,
        string resolutionCode,
        string? notes,
        DateTimeOffset resolvedAt)
    {
        if (state.Status != CaseStatus.Review)
            return CaseTransitionResult.Reject("case_not_in_review");
        if (string.IsNullOrWhiteSpace(resolverId))
            return CaseTransitionResult.Reject("resolver_required");
        if (string.IsNullOrWhiteSpace(resolutionCode))
            return CaseTransitionResult.Reject("resolution_code_required");

        return CaseTransitionResult.Accept(state with
        {
            Status = CaseStatus.Resolved,
            ResolutionCode = resolutionCode,
            ResolutionNotes = notes,
            ResolvedBy = resolverId,
            ResolvedAt = resolvedAt,
            Version = state.Version + 1,
        });
    }

    public static CaseTransitionResult Appeal(
        CaseState state,
        string reason,
        DateTimeOffset appealedAt)
    {
        if (state.Status != CaseStatus.Resolved)
            return CaseTransitionResult.Reject("case_not_appealable");
        if (string.IsNullOrWhiteSpace(reason))
            return CaseTransitionResult.Reject("appeal_reason_required");

        return CaseTransitionResult.Accept(state with
        {
            Status = CaseStatus.Appealed,
            AppealReason = reason,
            AppealedAt = appealedAt,
            Version = state.Version + 1,
        });
    }
}
