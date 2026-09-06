using BotFramework.Sdk.Events.Contracts;
using BotFramework.Sdk.Events.Meta;

namespace BotFramework.Sdk.Execution.DeferredOutcome;

/// <summary>
/// Declarative, transport-neutral definition for a game that debits a wager,
/// waits for an externally supplied outcome, and then either settles or refunds.
/// Keep <see cref="CalculatePayout"/> and event factories deterministic.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "SonarAnalyzer.CSharp",
    "S2326",
    Justification = "The marker type is intentionally part of the closed generic game-definition identity.")]
public sealed class DeferredOutcomeWagerDefinition<TGame, TOutcome>
    where TGame : IDeferredOutcomeWagerGame
{
    public DeferredOutcomeWagerDefinition(
        string gameId,
        string displayName,
        Func<DeferredOutcomeWager, TOutcome, long> calculatePayout,
        DeferredOutcomeWagerDailyQuota? dailyQuota = null,
        string? debitReason = null,
        string? payoutReason = null,
        string? refundReason = null,
        IEnumerable<string>? entropyNames = null,
        Func<DeferredOutcomeWagerPlaced, IReadOnlyList<IDomainEvent>>? createPlacedEvents = null,
        Func<DeferredOutcomeWagerResolved<TOutcome>, IReadOnlyList<IDomainEvent>>? createResolvedEvents = null,
        Func<DeferredOutcomeWagerAborted, IReadOnlyList<IDomainEvent>>? createAbortedEvents = null,
        GameDefinition? game = null,
        bool publishCompletionEvent = true)
    {
        GameId = RequireText(gameId, nameof(gameId));
        DisplayName = RequireText(displayName, nameof(displayName));
        CalculatePayout = calculatePayout ?? throw new ArgumentNullException(nameof(calculatePayout));
        DailyQuota = dailyQuota;
        DebitReason = debitReason ?? $"{GameId}.bet";
        PayoutReason = payoutReason ?? $"{GameId}.payout";
        RefundReason = refundReason ?? $"{GameId}.delivery_failed.refund";
        EntropyNames = ValidateEntropyNames(entropyNames);
        Game = game ?? new GameDefinition(
            GameId,
            DisplayName,
            GameCapabilities.DeferredOutcomeWager,
            dailyQuotaId: dailyQuota?.Id,
            entropyNames: EntropyNames);
        if (!string.Equals(Game.GameId, GameId, StringComparison.Ordinal))
            throw new ArgumentException("Game manifest id must match the wager game id.", nameof(game));
        if (!Game.Capabilities.HasFlag(GameCapabilities.DeferredOutcomeWager))
        {
            throw new ArgumentException(
                $"Game '{GameId}' must declare {nameof(GameCapabilities.DeferredOutcomeWager)} capability.",
                nameof(game));
        }
        if (!string.Equals(Game.DailyQuotaId, dailyQuota?.Id, StringComparison.Ordinal))
            throw new ArgumentException("Game manifest quota must match the wager quota.", nameof(game));
        if (!Game.EntropyNames.SequenceEqual(EntropyNames, StringComparer.Ordinal))
            throw new ArgumentException("Game manifest entropy names must match the wager entropy names.", nameof(game));
        CreatePlacedEvents = createPlacedEvents;
        CreateResolvedEvents = createResolvedEvents;
        CreateAbortedEvents = createAbortedEvents;
        PublishCompletionEvent = publishCompletionEvent;
    }

    public string GameId { get; }
    public string DisplayName { get; }
    public Func<DeferredOutcomeWager, TOutcome, long> CalculatePayout { get; }
    public DeferredOutcomeWagerDailyQuota? DailyQuota { get; }
    public string DebitReason { get; }
    public string PayoutReason { get; }
    public string RefundReason { get; }
    public IReadOnlyList<string> EntropyNames { get; }
    public GameDefinition Game { get; }
    public Func<DeferredOutcomeWagerPlaced, IReadOnlyList<IDomainEvent>>? CreatePlacedEvents { get; }
    public Func<DeferredOutcomeWagerResolved<TOutcome>, IReadOnlyList<IDomainEvent>>? CreateResolvedEvents { get; }
    public Func<DeferredOutcomeWagerAborted, IReadOnlyList<IDomainEvent>>? CreateAbortedEvents { get; }
    public bool PublishCompletionEvent { get; }

    internal IReadOnlyList<IDomainEvent> PlacedEvents(DeferredOutcomeWagerPlaced context) =>
        CreatePlacedEvents?.Invoke(context) ?? [];

    internal IReadOnlyList<IDomainEvent> ResolvedEvents(DeferredOutcomeWagerResolved<TOutcome> context)
    {
        var custom = CreateResolvedEvents?.Invoke(context) ?? [];
        if (!PublishCompletionEvent)
            return custom;

        var completion = GameCompletionEventFactory.Create(new(
            new GameCommandContext(context.Wager.UserId, context.DisplayName, context.Wager.ChatId, context.CommandId),
            GameId,
            context.Wager.Amount,
            context.Payout,
            context.OccurredAt));
        return [completion, .. custom];
    }

    internal IReadOnlyList<IDomainEvent> AbortedEvents(DeferredOutcomeWagerAborted context) =>
        CreateAbortedEvents?.Invoke(context) ?? [];

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value;

    private static string[] ValidateEntropyNames(IEnumerable<string>? names)
    {
        if (names is null)
            return [];

        var copy = names.ToArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Entropy names cannot be empty.", nameof(names));
        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Entropy names must be unique.", nameof(names));
        return copy;
    }
}
