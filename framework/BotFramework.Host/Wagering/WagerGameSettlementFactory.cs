using BotFramework.Contracts.Wagering;

namespace BotFramework.Host.Wagering;

public sealed class WagerGameSettlementFactory(IEnumerable<IWagerPayoutPolicy> policies)
    : IWagerSettlementCommandFactory
{
    public string GameId => "*";

    public WagerSettlementPlan Create(
        WagerOperation operation,
        GameOutcomeDeclared outcome,
        WagerTermsSnapshot terms)
    {
        var policy = policies.SingleOrDefault(x =>
            string.Equals(x.GameId, operation.GameId, StringComparison.Ordinal));
        if (policy is null)
            throw new InvalidOperationException($"No payout policy is registered for '{operation.GameId}'.");

        var payout = policy.CalculatePayout(operation, outcome, terms);
        if (payout < 0)
            throw new InvalidOperationException($"Payout for '{operation.GameId}' cannot be negative.");

        return new WagerSettlementPlan(payout);
    }
}
