namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>A player wager resolved within one atomic execution.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S2326",
    Justification = "The marker type is intentionally part of the closed generic command identity.")]
public sealed record InstantWagerCommand<TGame>(GameCommandContext Context, long Amount)
    : IPlayerGameCommand
    where TGame : IInstantWagerGame
{
    public long UserId => Context.UserId;
    public string DisplayName => Context.DisplayName;
    public long ChatId => Context.ChatId;
    public string CommandId => Context.CommandId;
}
