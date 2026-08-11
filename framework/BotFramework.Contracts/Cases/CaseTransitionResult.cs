namespace BotFramework.Contracts.Cases;

public sealed record CaseTransitionResult(
    bool Accepted,
    CaseState? State,
    string? ErrorCode = null)
{
    public static CaseTransitionResult Reject(string errorCode) => new(false, null, errorCode);
    public static CaseTransitionResult Accept(CaseState state) => new(true, state);
}
