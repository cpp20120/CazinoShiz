using BotFramework.Contracts.Wagering;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Wagering;

internal static class BlackjackWagerPayoutPolicy
{
    public const string RulesVersion = "blackjack.v1";

    public static long Calculate(GameOutcomeDeclared outcome, WagerTermsSnapshot terms)
    {
        if (!string.Equals(terms.RulesVersion, RulesVersion, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Unsupported blackjack payout rules version '{terms.RulesVersion}'.");
        if (terms.Stake <= 0)
            throw new InvalidOperationException("Blackjack wager stake must be positive.");
        if (!Enum.TryParse<BlackjackOutcome>(outcome.OutcomeCode, ignoreCase: true, out var result))
            throw new InvalidOperationException(
                $"Unsupported blackjack outcome '{outcome.OutcomeCode}'.");

        return result switch
        {
            BlackjackOutcome.PlayerBlackjack => checked(terms.Stake + terms.Stake * 3 / 2),
            BlackjackOutcome.PlayerWin or BlackjackOutcome.DealerBust => checked(terms.Stake * 2),
            BlackjackOutcome.Push => terms.Stake,
            BlackjackOutcome.PlayerBust or BlackjackOutcome.DealerWin => 0,
            _ => throw new InvalidOperationException($"Unsupported blackjack outcome '{result}'."),
        };
    }
}
