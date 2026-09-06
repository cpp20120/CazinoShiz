using BotFramework.Sdk.Events.Contracts;
using BotFramework.Sdk.Events.Meta;

namespace BotFramework.Sdk.Execution.InstantWager;

/// <summary>
/// Declarative model for a wager that is resolved immediately. The outcome and
/// payout delegates must be deterministic for their supplied inputs.
/// </summary>
public sealed class InstantWagerDefinition<TGame, TOutcome>
    where TGame : IInstantWagerGame
{
    public InstantWagerDefinition(
        GameDefinition game,
        Func<InstantWagerResolutionContext<TGame>, TOutcome> resolveOutcome,
        Func<InstantWagerSettlement, TOutcome, long> calculatePayout,
        string? debitReason = null,
        string? payoutReason = null,
        bool publishCompletionEvent = true,
        Func<InstantWagerResolved<TOutcome>, IReadOnlyList<IDomainEvent>>? createEvents = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (!game.Capabilities.HasFlag(GameCapabilities.InstantWager))
        {
            throw new ArgumentException(
                $"Game '{game.GameId}' must declare {nameof(GameCapabilities.InstantWager)} capability.",
                nameof(game));
        }
        if (game.StakeLimits is null)
            throw new ArgumentException("An instant wager game requires stake limits.", nameof(game));

        Game = game;
        ResolveOutcome = resolveOutcome ?? throw new ArgumentNullException(nameof(resolveOutcome));
        CalculatePayout = calculatePayout ?? throw new ArgumentNullException(nameof(calculatePayout));
        DebitReason = debitReason ?? $"{game.GameId}.bet";
        PayoutReason = payoutReason ?? $"{game.GameId}.payout";
        PublishCompletionEvent = publishCompletionEvent;
        CreateEvents = createEvents;
    }

    public GameDefinition Game { get; }
    public Func<InstantWagerResolutionContext<TGame>, TOutcome> ResolveOutcome { get; }
    public Func<InstantWagerSettlement, TOutcome, long> CalculatePayout { get; }
    public string DebitReason { get; }
    public string PayoutReason { get; }
    public bool PublishCompletionEvent { get; }
    public Func<InstantWagerResolved<TOutcome>, IReadOnlyList<IDomainEvent>>? CreateEvents { get; }

    internal IReadOnlyList<IDomainEvent> Events(InstantWagerResolved<TOutcome> context)
    {
        var custom = CreateEvents?.Invoke(context) ?? [];
        if (!PublishCompletionEvent)
            return custom;

        var completion = GameCompletionEventFactory.Create(new(
            context.Wager.Player,
            Game.GameId,
            context.Wager.Amount,
            context.Payout,
            context.Wager.OccurredAt));
        return [completion, .. custom];
    }
}
