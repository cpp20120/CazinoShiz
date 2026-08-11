namespace BotFramework.Contracts.Risk;

/// <summary>
/// Policy output, not a money mutation. A workflow decides whether a Hold or
/// Case should be opened after receiving this decision.
/// </summary>
public sealed record RiskDecision(
    RiskDecisionKind Kind,
    string PolicyVersion,
    string? ReasonCode = null,
    string? CaseId = null,
    string? HoldId = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyDictionary<string, string>? Evidence = null)
{
    public static RiskDecision Allow(string policyVersion) =>
        new(RiskDecisionKind.Allow, policyVersion);

    public static RiskDecision Deny(string policyVersion, string reasonCode) =>
        new(RiskDecisionKind.Deny, policyVersion, reasonCode);

    public static RiskDecision Review(string policyVersion, string reasonCode, string? caseId = null) =>
        new(RiskDecisionKind.Review, policyVersion, reasonCode, caseId);

    public static RiskDecision Hold(string policyVersion, string reasonCode, string? holdId = null,
        DateTimeOffset? expiresAt = null) =>
        new(RiskDecisionKind.Hold, policyVersion, reasonCode, HoldId: holdId, ExpiresAt: expiresAt);
}
