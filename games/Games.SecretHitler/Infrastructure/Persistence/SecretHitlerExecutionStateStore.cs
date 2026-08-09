using System.Text.Json;
using BotFramework.Host.Execution;
using BotFramework.Host.Contracts.Economics;
using Games.SecretHitler.Application.Execution;

namespace Games.SecretHitler.Infrastructure.Persistence;

public sealed class SecretHitlerExecutionStateStore<TCommand>(IEconomicsService economics)
    : IGameStateStore<TCommand, SecretHitlerExecutionState>
    where TCommand : ISecretHitlerExecutionCommand
{
    public async Task<SecretHitlerExecutionState> LoadAsync(
        TCommand command, IGameExecutionContext context, CancellationToken ct)
    {
        SecretHitlerGame? game;
        if (string.IsNullOrEmpty(command.InviteCode))
        {
            game = await LoadOpenByChatAsync(command.PublicChatId, context, ct);
        }
        else
        {
            game = await LoadGameAsync(command.InviteCode, context, ct);
        }

        var players = game is null ? [] : await LoadPlayersAsync(game.InviteCode, context, ct);
        var actorAlreadyInGame = await context.QuerySingleOrDefaultAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM secret_hitler_players WHERE user_id=@ActorUserId)",
            new { command.ActorUserId }, ct);
        int? actorBalance = null;
        if (command.EnsureActorWallet)
        {
            await economics.EnsureUserAsync(command.ActorUserId, command.ActorChatId, command.DisplayName, ct);
            actorBalance = await economics.GetBalanceAsync(command.ActorUserId, command.ActorChatId, ct);
        }
        return new(game, players, actorBalance, actorAlreadyInGame,
            string.IsNullOrEmpty(command.InviteCode) && game is not null);
    }

    public async Task SaveAsync(
        TCommand command, SecretHitlerExecutionState state, IGameExecutionContext context, CancellationToken ct)
    {
        if (state.Game is not { } game) return;
        await context.ExecuteAsync("""
            INSERT INTO secret_hitler_games
                (invite_code,host_user_id,chat_id,status,phase,liberal_policies,fascist_policies,
                 election_tracker,current_president_position,nominated_chancellor_position,
                 last_elected_president_position,last_elected_chancellor_position,deck_state,
                 discard_state,president_draw,chancellor_received,winner,win_reason,buy_in,pot,
                 state_message_id,created_at,last_action_at)
            VALUES
                (@InviteCode,@HostUserId,@ChatId,@Status,@Phase,@LiberalPolicies,@FascistPolicies,
                 @ElectionTracker,@CurrentPresidentPosition,@NominatedChancellorPosition,
                 @LastElectedPresidentPosition,@LastElectedChancellorPosition,@DeckState,
                 @DiscardState,@PresidentDraw,@ChancellorReceived,@Winner,@WinReason,@BuyIn,@Pot,
                 @StateMessageId,@CreatedAt,@LastActionAt)
            ON CONFLICT (invite_code) DO UPDATE SET
                host_user_id=EXCLUDED.host_user_id,chat_id=EXCLUDED.chat_id,status=EXCLUDED.status,
                phase=EXCLUDED.phase,liberal_policies=EXCLUDED.liberal_policies,
                fascist_policies=EXCLUDED.fascist_policies,election_tracker=EXCLUDED.election_tracker,
                current_president_position=EXCLUDED.current_president_position,
                nominated_chancellor_position=EXCLUDED.nominated_chancellor_position,
                last_elected_president_position=EXCLUDED.last_elected_president_position,
                last_elected_chancellor_position=EXCLUDED.last_elected_chancellor_position,
                deck_state=EXCLUDED.deck_state,discard_state=EXCLUDED.discard_state,
                president_draw=EXCLUDED.president_draw,chancellor_received=EXCLUDED.chancellor_received,
                winner=EXCLUDED.winner,win_reason=EXCLUDED.win_reason,buy_in=EXCLUDED.buy_in,
                pot=EXCLUDED.pot,state_message_id=EXCLUDED.state_message_id,
                last_action_at=EXCLUDED.last_action_at
            """, GameParameters(game), ct);
        await context.ExecuteAsync("DELETE FROM secret_hitler_players WHERE invite_code=@InviteCode",
            new { game.InviteCode }, ct);
        foreach (var player in state.Players)
        {
            await context.ExecuteAsync("""
                INSERT INTO secret_hitler_players
                    (invite_code,position,user_id,display_name,chat_id,role,is_alive,last_vote,
                     state_message_id,joined_at,wager_bet_id)
                VALUES
                    (@InviteCode,@Position,@UserId,@DisplayName,@ChatId,@Role,@IsAlive,@LastVote,
                     @StateMessageId,@JoinedAt,@WagerBetId)
                """, PlayerParameters(player), ct);
        }
    }

    private static async Task<SecretHitlerGame?> LoadGameAsync(
        string code, IGameExecutionContext context, CancellationToken ct)
    {
        var json = await context.QuerySingleOrDefaultAsync<string>(GameSelect +
            " WHERE invite_code=@code FOR UPDATE", new { code }, ct);
        return json is null ? null : JsonSerializer.Deserialize<SecretHitlerGame>(json);
    }

    private static async Task<SecretHitlerGame?> LoadOpenByChatAsync(
        long chatId, IGameExecutionContext context, CancellationToken ct)
    {
        var json = await context.QuerySingleOrDefaultAsync<string>(GameSelect +
            " WHERE chat_id=@chatId AND status NOT IN (@Closed,@Completed) ORDER BY created_at DESC LIMIT 1 FOR UPDATE",
            new { chatId, Closed = (int)ShStatus.Closed, Completed = (int)ShStatus.Completed }, ct);
        return json is null ? null : JsonSerializer.Deserialize<SecretHitlerGame>(json);
    }

    private static async Task<List<SecretHitlerPlayer>> LoadPlayersAsync(
        string code, IGameExecutionContext context, CancellationToken ct)
    {
        var json = await context.QuerySingleOrDefaultAsync<string>("""
            SELECT COALESCE(json_agg(json_build_object(
                'InviteCode',invite_code,'Position',position,'UserId',user_id,
                'DisplayName',display_name,'ChatId',chat_id,'Role',role,'IsAlive',is_alive,
                'LastVote',last_vote,'StateMessageId',state_message_id,'JoinedAt',joined_at,
                'WagerBetId',wager_bet_id)
                ORDER BY position),'[]'::json)::text
            FROM (SELECT * FROM secret_hitler_players WHERE invite_code=@code FOR UPDATE) locked_players
            """, new { code }, ct);
        return JsonSerializer.Deserialize<List<SecretHitlerPlayer>>(json ?? "[]") ?? [];
    }

    private const string GameSelect = """
        SELECT json_build_object(
            'InviteCode',invite_code,'HostUserId',host_user_id,'ChatId',chat_id,
            'Status',status,'Phase',phase,'LiberalPolicies',liberal_policies,
            'FascistPolicies',fascist_policies,'ElectionTracker',election_tracker,
            'CurrentPresidentPosition',current_president_position,
            'NominatedChancellorPosition',nominated_chancellor_position,
            'LastElectedPresidentPosition',last_elected_president_position,
            'LastElectedChancellorPosition',last_elected_chancellor_position,
            'DeckState',deck_state,'DiscardState',discard_state,'PresidentDraw',president_draw,
            'ChancellorReceived',chancellor_received,'Winner',winner,'WinReason',win_reason,
            'BuyIn',buy_in,'Pot',pot,'StateMessageId',state_message_id,
            'CreatedAt',created_at,'LastActionAt',last_action_at)::text
        FROM secret_hitler_games
        """;

    private static object GameParameters(SecretHitlerGame game) => new
    {
        game.InviteCode, game.HostUserId, game.ChatId, Status = (int)game.Status, Phase = (int)game.Phase,
        game.LiberalPolicies, game.FascistPolicies, game.ElectionTracker,
        game.CurrentPresidentPosition, game.NominatedChancellorPosition,
        game.LastElectedPresidentPosition, game.LastElectedChancellorPosition,
        game.DeckState, game.DiscardState, game.PresidentDraw, game.ChancellorReceived,
        Winner = (int)game.Winner, WinReason = (int)game.WinReason,
        game.BuyIn, game.Pot, game.StateMessageId, game.CreatedAt, game.LastActionAt,
    };

    private static object PlayerParameters(SecretHitlerPlayer player) => new
    {
        player.InviteCode, player.Position, player.UserId, player.DisplayName, player.ChatId,
        Role = (int)player.Role, player.IsAlive, LastVote = (int)player.LastVote,
        player.StateMessageId, player.JoinedAt,
        player.WagerBetId,
    };
}
