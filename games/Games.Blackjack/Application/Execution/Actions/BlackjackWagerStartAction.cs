using BotFramework.Sdk.Execution;
using Games.Blackjack.Contracts.Domain.Results;
using Games.Blackjack.Contracts.Integration;
using Games.Blackjack.Domain.Results;

namespace Games.Blackjack.Application.Execution;

public sealed class BlackjackWagerStartAction
    : IGameAction<BlackjackWagerStart, BlackjackWagerState, BlackjackWagerResult>
{
    public GameDecision<BlackjackWagerState, BlackjackWagerResult> Decide(
        GameActionInput<BlackjackWagerState, BlackjackWagerStart> input)
    {
        var command = input.Command;
        if (string.IsNullOrWhiteSpace(command.BetId)
            || string.IsNullOrWhiteSpace(command.PlayerId)
            || command.HandTimeoutMs <= 0)
        {
            return BlackjackWagerActionSupport.Reject(
                input.State,
                BlackjackError.InvalidBet,
                "invalid_wager_start");
        }

        if (input.State.Status == TurnGameStatus.Active)
            return BlackjackWagerActionSupport.Reject(
                input.State,
                BlackjackError.HandInProgress,
                "hand_in_progress");
        if (input.State.Outcome is not null)
            return BlackjackWagerActionSupport.Reject(
                input.State,
                BlackjackError.HandInProgress,
                "wager_already_completed");

        var deck = BlackjackDecisionRules.BuildShuffledDeck(input.Entropy);
        var playerCards = Deck.Draw(ref deck, 2);
        var dealerCards = Deck.Draw(ref deck, 2);
        var deadline = input.UtcNow.AddMilliseconds(command.HandTimeoutMs);
        var active = input.State with
        {
            Revision = checked(input.State.Revision + 1),
            BetId = command.BetId,
            PlayerId = command.PlayerId,
            ChatId = command.ChatId,
            DisplayName = command.DisplayName,
            Status = TurnGameStatus.Active,
            CurrentPlayerId = command.PlayerId,
            TurnDeadline = deadline,
            PlayerCards = playerCards,
            DealerCards = dealerCards,
            DeckState = deck,
            StateMessageId = null,
            Outcome = null,
            OutcomeEvidence = null,
        };

        if (BlackjackHandValue.IsNaturalBlackjack(playerCards))
        {
            active = active with { Revision = input.State.Revision };
            var resolution = BlackjackWagerRules.Resolve(playerCards, dealerCards, deck);
            return BlackjackWagerActionSupport.Complete(
                input,
                active,
                resolution.DeckState,
                resolution.DealerCards,
                resolution,
                cancelTimeout: false);
        }

        var timeout = new BlackjackWagerTimeout(
            command.OperationId,
            command.BetId,
            command.PlayerId,
            command.ChatId,
            command.DisplayName,
            $"{command.CommandId}:timeout",
            input.UtcNow);
        return new GameDecision<BlackjackWagerState, BlackjackWagerResult>(
            DecisionStatus.Accepted,
            active,
            new BlackjackWagerResult(
                BlackjackError.None,
                BlackjackWagerRules.BuildSnapshot(active)),
            [],
            [],
            [],
            [],
            [ScheduleEffect.ScheduleCommand(
                BlackjackWagerActionSupport.TimeoutScheduleId,
                deadline,
                timeout)]);
    }
}
