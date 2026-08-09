using BotFramework.Sdk.Execution;
using Games.SecretHitler.Application.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Wagering;

public sealed class SecretHitlerWagerJoinAction
    : IGameAction<ShJoinCommand, SecretHitlerWagerState, ShJoinResult>
{
    public GameDecision<SecretHitlerWagerState, ShJoinResult> Decide(
        GameActionInput<SecretHitlerWagerState, ShJoinCommand> input)
    {
        var c = input.Command;
        if (c.WagerBetId is null) return Reject(input.State, ShError.NotInGame);
        if (input.State.Game is not { } source || source.Status is ShStatus.Closed or ShStatus.Completed)
            return Reject(input.State, ShError.GameNotFound);
        if (source.Status != ShStatus.Lobby) return Reject(input.State, ShError.GameInProgress);
        if (input.State.Players.Any(player => player.UserId == c.ActorUserId))
            return Reject(input.State, ShError.AlreadyInGame);
        if (input.State.Players.Count >= ShRoleDealer.MaxPlayers)
            return Reject(input.State, ShError.GameFull);
        if (c.BuyIn != source.BuyIn) return Reject(input.State, ShError.NotEnoughCoins);

        var state = new SecretHitlerWagerState(
            SecretHitlerExecutionRules.Clone(source), input.State.Players.Select(SecretHitlerExecutionRules.Clone).ToList());
        var position = 0;
        var used = state.Players.Select(player => player.Position).ToHashSet();
        while (used.Contains(position)) position++;
        var now = input.UtcNow.ToUnixTimeMilliseconds();
        state.Players.Add(new SecretHitlerPlayer
        {
            InviteCode = source.InviteCode, Position = position, UserId = c.ActorUserId,
            DisplayName = c.DisplayName, ChatId = c.ActorChatId, IsAlive = true,
            JoinedAt = now, WagerBetId = c.WagerBetId,
        });
        state.Game!.Pot += c.BuyIn;
        state.Game.LastActionAt = now;
        return new(DecisionStatus.Accepted, state,
            new(ShError.None, new ShGameSnapshot(state.Game, state.Players), state.Players.Count, ShRoleDealer.MaxPlayers),
            [], [], [], [new SecretHitlerPlayerJoined(source.InviteCode, c.ActorUserId, position, c.BuyIn, now)], []);
    }

    private static GameDecision<SecretHitlerWagerState, ShJoinResult> Reject(
        SecretHitlerWagerState state, ShError error) =>
        new(DecisionStatus.Rejected, state, JoinFail(error), [], [], [], [], [], error.ToString());
}
