using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public sealed class ShJoinAction : IGameAction<ShJoinCommand, SecretHitlerExecutionState, ShJoinResult>
{
    public GameDecision<SecretHitlerExecutionState, ShJoinResult> Decide(
        GameActionInput<SecretHitlerExecutionState, ShJoinCommand> input)
    {
        if (input.Command.WagerBetId is null && input.State.ActorBalance < input.Command.BuyIn)
            return Reject(input.State, ShError.NotEnoughCoins);
        if (input.State.ActorAlreadyInGame) return Reject(input.State, ShError.AlreadyInGame);
        if (input.State.Game is not { } source || source.Status is ShStatus.Closed or ShStatus.Completed)
            return Reject(input.State, ShError.GameNotFound);
        if (source.Status != ShStatus.Lobby) return Reject(input.State, ShError.GameInProgress);
        if (input.State.Players.Count >= ShRoleDealer.MaxPlayers) return Reject(input.State, ShError.GameFull);

        var state = SecretHitlerExecutionRules.Clone(input.State);
        var position = 0;
        var used = state.Players.Select(p => p.Position).ToHashSet();
        while (used.Contains(position)) position++;
        var now = input.UtcNow.ToUnixTimeMilliseconds();
        state.Players.Add(new SecretHitlerPlayer
        {
            InviteCode = source.InviteCode, Position = position, UserId = input.Command.ActorUserId,
            DisplayName = input.Command.DisplayName, ChatId = input.Command.ActorChatId,
            IsAlive = true, JoinedAt = now, WagerBetId = input.Command.WagerBetId,
        });
        state.Game!.Pot += input.Command.BuyIn;
        state.Game.LastActionAt = now;
        return new(DecisionStatus.Accepted, state,
            new(ShError.None, SecretHitlerExecutionRules.Snapshot(state), state.Players.Count, ShRoleDealer.MaxPlayers),
            [], [], [], [new SecretHitlerPlayerJoined(source.InviteCode, input.Command.ActorUserId,
                position, input.Command.BuyIn, now)], [],
            CustomEffects: input.Command.WagerBetId is null
                ? [WalletEconomyEffect.Debit(input.Command.ActorUserId,
                    input.Command.ActorChatId, input.Command.BuyIn, "sh.join")]
                : []);
    }

    private static GameDecision<SecretHitlerExecutionState, ShJoinResult> Reject(
        SecretHitlerExecutionState state, ShError error) =>
        new(DecisionStatus.Rejected, state, JoinFail(error), [], [], [], [], [], error.ToString());
}
