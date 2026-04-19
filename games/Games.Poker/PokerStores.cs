// ─────────────────────────────────────────────────────────────────────────────
// PokerStores — Dapper-backed access for poker_tables + poker_seats. The
// domain mutates PokerTable/PokerSeat instances in place, so updates
// overwrite every field from the in-memory object.
//
// Concurrency: PokerService serializes all mutating operations behind a
// process-local SemaphoreSlim (same shape as the monolith). Distributed hosts
// would require a proper per-table lock or optimistic concurrency — out of
// scope for this port.
// ─────────────────────────────────────────────────────────────────────────────

using BotFramework.Host;
using Dapper;

namespace Games.Poker;

public interface IPokerTableStore
{
    Task<PokerTable?> FindAsync(string inviteCode, CancellationToken ct);
    Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct);
    Task InsertAsync(PokerTable table, CancellationToken ct);
    Task UpdateAsync(PokerTable table, CancellationToken ct);
    Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct);
}

public interface IPokerSeatStore
{
    Task<PokerSeat?> FindByUserAsync(long userId, CancellationToken ct);
    Task<List<PokerSeat>> ListByTableAsync(string inviteCode, CancellationToken ct);
    Task<int> CountByTableAsync(string inviteCode, long exceptUserId, CancellationToken ct);
    Task<bool> AnyForUserAsync(long userId, CancellationToken ct);
    Task InsertAsync(PokerSeat seat, CancellationToken ct);
    Task UpdateAsync(PokerSeat seat, CancellationToken ct);
    Task DeleteAsync(string inviteCode, int position, CancellationToken ct);
    Task UpsertStateMessageAsync(long userId, int messageId, CancellationToken ct);
}

public sealed class PokerTableStore(INpgsqlConnectionFactory connections) : IPokerTableStore
{
    public async Task<PokerTable?> FindAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TableRow>(new CommandDefinition(
            "SELECT * FROM poker_tables WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
        return row == null ? null : row.ToEntity();
    }

    public async Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM poker_tables WHERE invite_code = @inviteCode)",
            new { inviteCode },
            cancellationToken: ct));
    }

    public async Task InsertAsync(PokerTable t, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO poker_tables
                (invite_code, host_user_id, status, phase, small_blind, big_blind, pot,
                 community_cards, deck_state, button_seat, current_seat, current_bet,
                 min_raise, last_action_at, created_at)
            VALUES
                (@InviteCode, @HostUserId, @Status, @Phase, @SmallBlind, @BigBlind, @Pot,
                 @CommunityCards, @DeckState, @ButtonSeat, @CurrentSeat, @CurrentBet,
                 @MinRaise, @LastActionAt, @CreatedAt)
            """,
            new TableRow(
                t.InviteCode, t.HostUserId, (int)t.Status, (int)t.Phase,
                t.SmallBlind, t.BigBlind, t.Pot, t.CommunityCards, t.DeckState,
                t.ButtonSeat, t.CurrentSeat, t.CurrentBet, t.MinRaise,
                t.LastActionAt, t.CreatedAt),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(PokerTable t, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE poker_tables SET
                host_user_id = @HostUserId,
                status = @Status,
                phase = @Phase,
                small_blind = @SmallBlind,
                big_blind = @BigBlind,
                pot = @Pot,
                community_cards = @CommunityCards,
                deck_state = @DeckState,
                button_seat = @ButtonSeat,
                current_seat = @CurrentSeat,
                current_bet = @CurrentBet,
                min_raise = @MinRaise,
                last_action_at = @LastActionAt
            WHERE invite_code = @InviteCode
            """,
            new TableRow(
                t.InviteCode, t.HostUserId, (int)t.Status, (int)t.Phase,
                t.SmallBlind, t.BigBlind, t.Pot, t.CommunityCards, t.DeckState,
                t.ButtonSeat, t.CurrentSeat, t.CurrentBet, t.MinRaise,
                t.LastActionAt, t.CreatedAt),
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var codes = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT invite_code FROM poker_tables WHERE status = @active AND last_action_at < @cutoff",
            new { active = (int)PokerTableStatus.HandActive, cutoff = cutoffMs },
            cancellationToken: ct));
        return [.. codes];
    }

    private sealed record TableRow(
        string InviteCode, long HostUserId, int Status, int Phase,
        int SmallBlind, int BigBlind, int Pot, string CommunityCards, string DeckState,
        int ButtonSeat, int CurrentSeat, int CurrentBet, int MinRaise,
        long LastActionAt, long CreatedAt)
    {
        public PokerTable ToEntity() => new()
        {
            InviteCode = InviteCode,
            HostUserId = HostUserId,
            Status = (PokerTableStatus)Status,
            Phase = (PokerPhase)Phase,
            SmallBlind = SmallBlind,
            BigBlind = BigBlind,
            Pot = Pot,
            CommunityCards = CommunityCards,
            DeckState = DeckState,
            ButtonSeat = ButtonSeat,
            CurrentSeat = CurrentSeat,
            CurrentBet = CurrentBet,
            MinRaise = MinRaise,
            LastActionAt = LastActionAt,
            CreatedAt = CreatedAt,
        };
    }
}

public sealed class PokerSeatStore(INpgsqlConnectionFactory connections) : IPokerSeatStore
{
    private const string SelectColumns =
        "invite_code AS InviteCode, position AS Position, user_id AS UserId, display_name AS DisplayName, " +
        "stack AS Stack, hole_cards AS HoleCards, status AS Status, current_bet AS CurrentBet, " +
        "has_acted_round AS HasActedThisRound, chat_id AS ChatId, state_message_id AS StateMessageId, " +
        "joined_at AS JoinedAt";

    public async Task<PokerSeat?> FindByUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SeatRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM poker_seats WHERE user_id = @userId LIMIT 1",
            new { userId },
            cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<List<PokerSeat>> ListByTableAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<SeatRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM poker_seats WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
        return rows.Select(r => r.ToEntity()).ToList();
    }

    public async Task<int> CountByTableAsync(string inviteCode, long exceptUserId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM poker_seats WHERE invite_code = @inviteCode AND user_id <> @exceptUserId",
            new { inviteCode, exceptUserId },
            cancellationToken: ct));
    }

    public async Task<bool> AnyForUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM poker_seats WHERE user_id = @userId)",
            new { userId },
            cancellationToken: ct));
    }

    public async Task InsertAsync(PokerSeat s, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO poker_seats
                (invite_code, position, user_id, display_name, stack, hole_cards, status,
                 current_bet, has_acted_round, chat_id, state_message_id, joined_at)
            VALUES
                (@InviteCode, @Position, @UserId, @DisplayName, @Stack, @HoleCards, @Status,
                 @CurrentBet, @HasActedThisRound, @ChatId, @StateMessageId, @JoinedAt)
            """,
            SeatRow.From(s),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(PokerSeat s, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE poker_seats SET
                display_name = @DisplayName,
                stack = @Stack,
                hole_cards = @HoleCards,
                status = @Status,
                current_bet = @CurrentBet,
                has_acted_round = @HasActedThisRound,
                chat_id = @ChatId,
                state_message_id = @StateMessageId
            WHERE invite_code = @InviteCode AND position = @Position
            """,
            SeatRow.From(s),
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string inviteCode, int position, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM poker_seats WHERE invite_code = @inviteCode AND position = @position",
            new { inviteCode, position },
            cancellationToken: ct));
    }

    public async Task UpsertStateMessageAsync(long userId, int messageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE poker_seats SET state_message_id = @messageId WHERE user_id = @userId",
            new { userId, messageId },
            cancellationToken: ct));
    }

    private sealed record SeatRow(
        string InviteCode, int Position, long UserId, string DisplayName,
        int Stack, string HoleCards, int Status, int CurrentBet,
        bool HasActedThisRound, long ChatId, int? StateMessageId, long JoinedAt)
    {
        public static SeatRow From(PokerSeat s) => new(
            s.InviteCode, s.Position, s.UserId, s.DisplayName,
            s.Stack, s.HoleCards, (int)s.Status, s.CurrentBet,
            s.HasActedThisRound, s.ChatId, s.StateMessageId, s.JoinedAt);

        public PokerSeat ToEntity() => new()
        {
            InviteCode = InviteCode,
            Position = Position,
            UserId = UserId,
            DisplayName = DisplayName,
            Stack = Stack,
            HoleCards = HoleCards,
            Status = (PokerSeatStatus)Status,
            CurrentBet = CurrentBet,
            HasActedThisRound = HasActedThisRound,
            ChatId = ChatId,
            StateMessageId = StateMessageId,
            JoinedAt = JoinedAt,
        };
    }
}
