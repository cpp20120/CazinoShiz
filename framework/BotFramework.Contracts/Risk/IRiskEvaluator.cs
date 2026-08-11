namespace BotFramework.Contracts.Risk;

/// <summary>
/// Application policies implement this interface; the framework only carries
/// and orchestrates their decision.
/// </summary>
public interface IRiskEvaluator<TContext>
{
    Task<RiskDecision> EvaluateAsync(
        RiskEvaluation<TContext> evaluation,
        CancellationToken ct);
}
