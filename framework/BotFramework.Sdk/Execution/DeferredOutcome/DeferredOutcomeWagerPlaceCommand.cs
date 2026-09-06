namespace BotFramework.Sdk.Execution.DeferredOutcome;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S2326",
    Justification = "The marker and outcome types are intentionally part of the closed generic command identity.")]
public sealed record DeferredOutcomeWagerPlaceCommand<TGame, TOutcome>(
    long UserId,
    string DisplayName,
    long ChatId,
    long Amount,
    long MaximumAmount,
    string CommandId,
    string? BlockingGameId = null)
    : IDeferredOutcomeWagerCommand
    where TGame : IDeferredOutcomeWagerGame;
