namespace BotFramework.Host.Wagering;

public sealed record WagerGameResult(
    bool Accepted,
    string? OutcomeCode = null,
    string? Evidence = null,
    long Revision = 0,
    string? ErrorCode = null);
