using BotFramework.Sdk.Execution;
using static Games.SecretHitler.Domain.Rules.ShResultHelpers;

namespace Games.SecretHitler.Application.Execution;

public static class SecretHitlerExecutionRules
{
    public const string InviteEntropy = "invite-code";
    public static readonly IReadOnlyList<string> RoleEntropyNames =
        Enumerable.Range(0, 9).Select(i => $"role-{i}").ToArray();
    public static readonly IReadOnlyList<string> DeckEntropyNames =
        Enumerable.Range(0, 16).Select(i => $"deck-{i}").ToArray();
    public static readonly IReadOnlyList<string> ReshuffleEntropyNames =
        Enumerable.Range(0, 16).Select(i => $"reshuffle-{i}").ToArray();

    public static SecretHitlerExecutionState Clone(SecretHitlerExecutionState source) =>
        new(source.Game is null ? null : Clone(source.Game), source.Players.Select(Clone).ToList(),
            source.ActorBalance, source.ActorAlreadyInGame, source.ChatAlreadyHasGame);

    public static SecretHitlerGame Clone(SecretHitlerGame game) => new()
    {
        InviteCode = game.InviteCode, HostUserId = game.HostUserId, ChatId = game.ChatId,
        Status = game.Status, Phase = game.Phase, LiberalPolicies = game.LiberalPolicies,
        FascistPolicies = game.FascistPolicies, ElectionTracker = game.ElectionTracker,
        CurrentPresidentPosition = game.CurrentPresidentPosition,
        NominatedChancellorPosition = game.NominatedChancellorPosition,
        LastElectedPresidentPosition = game.LastElectedPresidentPosition,
        LastElectedChancellorPosition = game.LastElectedChancellorPosition,
        DeckState = game.DeckState, DiscardState = game.DiscardState,
        PresidentDraw = game.PresidentDraw, ChancellorReceived = game.ChancellorReceived,
        Winner = game.Winner, WinReason = game.WinReason, BuyIn = game.BuyIn, Pot = game.Pot,
        StateMessageId = game.StateMessageId, CreatedAt = game.CreatedAt, LastActionAt = game.LastActionAt,
    };

    public static SecretHitlerPlayer Clone(SecretHitlerPlayer player) => new()
    {
        InviteCode = player.InviteCode, Position = player.Position, UserId = player.UserId,
        DisplayName = player.DisplayName, ChatId = player.ChatId, Role = player.Role,
        IsAlive = player.IsAlive, LastVote = player.LastVote,
        StateMessageId = player.StateMessageId, JoinedAt = player.JoinedAt,
        WagerBetId = player.WagerBetId,
    };

    public static string InviteCode(double entropy)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var value = (int)(entropy * 33_554_432d);
        Span<char> chars = stackalloc char[5];
        for (var index = chars.Length - 1; index >= 0; index--)
        {
            chars[index] = alphabet[value & 31];
            value >>= 5;
        }
        return new string(chars);
    }

    public static IReadOnlyList<IGameEffect> Settle(
        SecretHitlerExecutionState state, List<SecretHitlerPayout> payouts,
        List<IDomainEvent>? wagerOutcomes = null)
    {
        var game = state.Game!;
        var winners = game.Winner switch
        {
            ShWinner.Liberals => state.Players.Where(p => p.Role == ShRole.Liberal).ToList(),
            ShWinner.Fascists => state.Players.Where(p => p.Role is ShRole.Fascist or ShRole.Hitler).ToList(),
            _ => [],
        };
        if (winners.Count == 0 || game.Pot == 0)
        {
            foreach (var player in state.Players.Where(player => player.WagerBetId is not null))
                wagerOutcomes?.Add(new SecretHitlerWagerOutcomeDeclared(
                    player.WagerBetId!, player.UserId.ToString(), "loss",
                    System.Text.Json.JsonSerializer.Serialize(new { payout = 0 }),
                    game.LastActionAt));
            return [];
        }
        var share = game.Pot / winners.Count;
        var remainder = game.Pot - share * winners.Count;
        var effects = new List<IGameEffect>(winners.Count);
        var payoutByPlayer = new Dictionary<long, int>();
        foreach (var winner in winners)
        {
            var amount = share + (remainder-- > 0 ? 1 : 0);
            payoutByPlayer[winner.UserId] = amount;
            if (winner.WagerBetId is null)
            {
                effects.Add(WalletEconomyEffect.Credit(winner.UserId, winner.ChatId, amount, "sh.winnings"));
                payouts.Add(new(winner.UserId, amount));
            }
        }
        foreach (var player in state.Players.Where(player => player.WagerBetId is not null))
        {
            var payout = payoutByPlayer.GetValueOrDefault(player.UserId);
            wagerOutcomes?.Add(new SecretHitlerWagerOutcomeDeclared(
                player.WagerBetId!, player.UserId.ToString(), payout > 0 ? "win" : "loss",
                System.Text.Json.JsonSerializer.Serialize(new { payout }), game.LastActionAt));
        }
        game.Pot = 0;
        return effects;
    }

    public static ShGameSnapshot Snapshot(SecretHitlerExecutionState state) => new(state.Game!, state.Players);
}
