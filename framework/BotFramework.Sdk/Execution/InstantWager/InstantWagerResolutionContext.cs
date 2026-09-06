namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>Deterministic inputs available while an instant wager resolves its outcome.</summary>
public sealed record InstantWagerResolutionContext<TGame>(
    InstantWagerCommand<TGame> Command,
    EntropyValue Entropy,
    DateTimeOffset OccurredAt)
    where TGame : IInstantWagerGame;
