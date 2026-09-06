namespace BotFramework.Sdk.Execution.DeferredOutcome;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S2326",
    Justification = "The marker type is intentionally part of the closed generic command identity.")]
public sealed record DeferredOutcomeWagerResolveCommand<TGame, TOutcome>(
    long UserId,
    string DisplayName,
    long ChatId,
    TOutcome Outcome,
    string CommandId)
    : IDeferredOutcomeWagerCommand
    where TGame : IDeferredOutcomeWagerGame;
