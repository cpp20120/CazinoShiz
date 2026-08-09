using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public sealed class ShCreateAction : IGameAction<ShCreateCommand, SecretHitlerExecutionState, ShCreateResult>
{
    public GameDecision<SecretHitlerExecutionState, ShCreateResult> Decide(
        GameActionInput<SecretHitlerExecutionState, ShCreateCommand> input)
    {
        if (input.Command.WagerBetId is null && input.State.ActorBalance < input.Command.BuyIn)
            return Reject(input.State, ShError.NotEnoughCoins);
        if (input.State.ActorAlreadyInGame) return Reject(input.State, ShError.AlreadyInGame);
        if (input.State.ChatAlreadyHasGame) return Reject(input.State, ShError.GameInProgress);
        var now = input.UtcNow.ToUnixTimeMilliseconds();
        var code = SecretHitlerExecutionRules.InviteCode(input.Entropy.GetDouble(SecretHitlerExecutionRules.InviteEntropy));
        var game = new SecretHitlerGame
        {
            InviteCode = code, HostUserId = input.Command.ActorUserId, ChatId = input.Command.PublicChatId,
            Status = ShStatus.Lobby, Phase = ShPhase.None, BuyIn = input.Command.BuyIn,
            Pot = input.Command.BuyIn, CreatedAt = now, LastActionAt = now,
        };
        var player = new SecretHitlerPlayer
        {
            InviteCode = code, Position = 0, UserId = input.Command.ActorUserId,
            DisplayName = input.Command.DisplayName, ChatId = input.Command.ActorChatId,
            IsAlive = true, JoinedAt = now, WagerBetId = input.Command.WagerBetId,
        };
        return new(DecisionStatus.Accepted,
            new(game, [player], input.State.ActorBalance, true, true),
            new(ShError.None, code, input.Command.BuyIn), [], [], [],
            [new SecretHitlerGameCreated(code, input.Command.ActorUserId, input.Command.BuyIn, now)], [],
            CustomEffects: input.Command.WagerBetId is null
                ? [WalletEconomyEffect.Debit(input.Command.ActorUserId,
                    input.Command.ActorChatId, input.Command.BuyIn, "sh.create")]
                : []);
    }

    private static GameDecision<SecretHitlerExecutionState, ShCreateResult> Reject(
        SecretHitlerExecutionState state, ShError error) =>
        new(DecisionStatus.Rejected, state, CreateFail(error), [], [], [], [], [], error.ToString());
}
