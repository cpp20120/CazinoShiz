using System.Text.Json;
using BotFramework.Host.Execution;
using Games.Poker.Application.Execution;
using Games.Poker.Application.Wagering;

namespace Games.Poker.Infrastructure.Persistence;

/// <summary>Relational table/seat store for Poker's economy-free wager state.</summary>
public sealed class PokerWagerStateStore<TCommand>
    : IGameStateStore<TCommand, PokerWagerState>
    where TCommand : IPokerExecutionCommand
{
    public async Task<PokerWagerState> LoadAsync(
        TCommand command, IGameExecutionContext context, CancellationToken ct)
    {
        var tableJson = await context.QuerySingleOrDefaultAsync<string>(TableSelect(command),
            new { command.InviteCode, command.ChatId, Closed = (int)PokerTableStatus.Closed }, ct);
        var table = tableJson is null ? null : JsonSerializer.Deserialize<PokerTable>(tableJson);
        var seats = table is null
            ? []
            : JsonSerializer.Deserialize<List<PokerSeat>>(await context.QuerySingleOrDefaultAsync<string>(SeatsSelect,
                new { table.InviteCode }, ct) ?? "[]") ?? [];
        return new(table, seats);
    }

    public async Task SaveAsync(
        TCommand command, PokerWagerState state, IGameExecutionContext context, CancellationToken ct)
    {
        if (state.Table is not { } table) return;
        await context.ExecuteAsync("""
            INSERT INTO poker_tables
                (invite_code,chat_id,host_user_id,status,phase,small_blind,big_blind,pot,
                 community_cards,deck_state,button_seat,current_seat,current_bet,min_raise,
                 state_message_id,last_action_at,created_at)
            VALUES
                (@InviteCode,@ChatId,@HostUserId,@Status,@Phase,@SmallBlind,@BigBlind,@Pot,
                 @CommunityCards,@DeckState,@ButtonSeat,@CurrentSeat,@CurrentBet,@MinRaise,
                 @StateMessageId,@LastActionAt,@CreatedAt)
            ON CONFLICT (invite_code) DO UPDATE SET
                chat_id=EXCLUDED.chat_id,host_user_id=EXCLUDED.host_user_id,status=EXCLUDED.status,
                phase=EXCLUDED.phase,small_blind=EXCLUDED.small_blind,big_blind=EXCLUDED.big_blind,
                pot=EXCLUDED.pot,community_cards=EXCLUDED.community_cards,deck_state=EXCLUDED.deck_state,
                button_seat=EXCLUDED.button_seat,current_seat=EXCLUDED.current_seat,
                current_bet=EXCLUDED.current_bet,min_raise=EXCLUDED.min_raise,
                state_message_id=EXCLUDED.state_message_id,last_action_at=EXCLUDED.last_action_at
            """, TableParameters(table), ct);
        await context.ExecuteAsync("DELETE FROM poker_seats WHERE invite_code=@InviteCode",
            new { table.InviteCode }, ct);
        foreach (var seat in state.Seats)
        {
            await context.ExecuteAsync("""
                INSERT INTO poker_seats
                    (invite_code,position,user_id,display_name,stack,hole_cards,status,current_bet,
                     total_committed,has_acted_round,chat_id,state_message_id,joined_at,wager_bet_id)
                VALUES
                    (@InviteCode,@Position,@UserId,@DisplayName,@Stack,@HoleCards,@Status,@CurrentBet,
                     @TotalCommitted,@HasActedThisRound,@ChatId,@StateMessageId,@JoinedAt,@WagerBetId)
                """, SeatParameters(seat), ct);
        }
    }

    private static string TableSelect(TCommand command) => $"""
        SELECT json_build_object(
            'InviteCode',invite_code,'ChatId',chat_id,'HostUserId',host_user_id,
            'Status',status,'Phase',phase,'SmallBlind',small_blind,'BigBlind',big_blind,
            'Pot',pot,'CommunityCards',community_cards,'DeckState',deck_state,
            'ButtonSeat',button_seat,'CurrentSeat',current_seat,'CurrentBet',current_bet,
            'MinRaise',min_raise,'StateMessageId',state_message_id,'LastActionAt',last_action_at,
            'CreatedAt',created_at)::text
        FROM poker_tables
        WHERE {(string.IsNullOrEmpty(command.InviteCode) ? "chat_id=@ChatId AND status<>@Closed" : "invite_code=@InviteCode")}
        ORDER BY created_at DESC LIMIT 1 FOR UPDATE
        """;

    private const string SeatsSelect = """
        SELECT COALESCE(json_agg(json_build_object(
            'InviteCode',invite_code,'Position',position,'UserId',user_id,'DisplayName',display_name,
            'Stack',stack,'HoleCards',hole_cards,'Status',status,'CurrentBet',current_bet,
            'TotalCommitted',total_committed,'HasActedThisRound',has_acted_round,
            'ChatId',chat_id,'StateMessageId',state_message_id,'JoinedAt',joined_at,
            'WagerBetId',wager_bet_id)
            ORDER BY position),'[]'::json)::text
        FROM (SELECT * FROM poker_seats WHERE invite_code=@InviteCode FOR UPDATE) locked_seats
        """;

    private static object TableParameters(PokerTable table) => new
    {
        table.InviteCode, table.ChatId, table.HostUserId,
        Status = (int)table.Status, Phase = (int)table.Phase,
        table.SmallBlind, table.BigBlind, table.Pot, table.CommunityCards, table.DeckState,
        table.ButtonSeat, table.CurrentSeat, table.CurrentBet, table.MinRaise,
        table.StateMessageId, table.LastActionAt, table.CreatedAt,
    };

    private static object SeatParameters(PokerSeat seat) => new
    {
        seat.InviteCode, seat.Position, seat.UserId, seat.DisplayName, seat.Stack, seat.HoleCards,
        Status = (int)seat.Status, seat.CurrentBet, seat.TotalCommitted, seat.HasActedThisRound,
        seat.ChatId, seat.StateMessageId, seat.JoinedAt, seat.WagerBetId,
    };
}
