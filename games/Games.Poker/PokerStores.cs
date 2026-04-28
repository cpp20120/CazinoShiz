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
    Task<PokerTable?> FindOpenByChatAsync(long chatId, CancellationToken ct);
    Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct);
    Task InsertAsync(PokerTable table, CancellationToken ct);
    Task UpdateAsync(PokerTable table, CancellationToken ct);
    Task UpsertStateMessageAsync(string inviteCode, int messageId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListStuckCodesAsync(long cutoffMs, CancellationToken ct);
}

public interface IPokerSeatStore
{
    Task<PokerSeat?> FindByUserAsync(long userId, CancellationToken ct);
    Task<PokerSeat?> FindByUserInTableAsync(long userId, string inviteCode, CancellationToken ct);
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
    private const string SelectColumns =
        "invite_code AS InviteCode, chat_id AS ChatId, host_user_id AS HostUserId, status AS Status, phase AS Phase, " +
        "small_blind AS SmallBlind, big_blind AS BigBlind, pot AS Pot, community_cards AS CommunityCards, " +
        "deck_state AS DeckState, button_seat AS ButtonSeat, current_seat AS CurrentSeat, current_bet AS CurrentBet, " +
        "min_raise AS MinRaise, state_message_id AS StateMessageId, last_action_at AS LastActionAt, created_at AS CreatedAt";

    public async Task<PokerTable?> FindAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TableRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM poker_tables WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
        return row == null ? null : row.ToEntity();
    }

    public async Task<PokerTable?> FindOpenByChatAsync(long chatId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<TableRow>(new CommandDefinition(
            $"""
            SELECT {SelectColumns}
            FROM poker_tables
            WHERE chat_id = @chatId AND status <> @closed
            ORDER BY created_at DESC
            LIMIT 1
            """,
            new { chatId, closed = (int)PokerTableStatus.Closed },
            cancellationToken: ct));
        return row?.ToEntity();
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
                (invite_code, chat_id, host_user_id, status, phase, small_blind, big_blind, pot,
                 community_cards, deck_state, button_seat, current_seat, current_bet,
                 min_raise, state_message_id, last_action_at, created_at)
            VALUES
                (@InviteCode, @ChatId, @HostUserId, @Status, @Phase, @SmallBlind, @BigBlind, @Pot,
                 @CommunityCards, @DeckState, @ButtonSeat, @CurrentSeat, @CurrentBet,
                 @MinRaise, @StateMessageId, @LastActionAt, @CreatedAt)
            """,
            new TableRow(
                t.InviteCode, t.ChatId, t.HostUserId, (int)t.Status, (int)t.Phase,
                t.SmallBlind, t.BigBlind, t.Pot, t.CommunityCards, t.DeckState,
                t.ButtonSeat, t.CurrentSeat, t.CurrentBet, t.MinRaise,
                t.StateMessageId, t.LastActionAt, t.CreatedAt),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(PokerTable t, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE poker_tables SET
                chat_id = @ChatId,
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
                state_message_id = @StateMessageId,
                last_action_at = @LastActionAt
            WHERE invite_code = @InviteCode
            """,
            new TableRow(
                t.InviteCode, t.ChatId, t.HostUserId, (int)t.Status, (int)t.Phase,
                t.SmallBlind, t.BigBlind, t.Pot, t.CommunityCards, t.DeckState,
                t.ButtonSeat, t.CurrentSeat, t.CurrentBet, t.MinRaise,
                t.StateMessageId, t.LastActionAt, t.CreatedAt),
            cancellationToken: ct));
    }

    public async Task UpsertStateMessageAsync(string inviteCode, int messageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE poker_tables SET state_message_id = @messageId WHERE invite_code = @inviteCode",
            new { inviteCode, messageId },
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
        string InviteCode, long ChatId, long HostUserId, int Status, int Phase,
        int SmallBlind, int BigBlind, int Pot, string CommunityCards, string DeckState,
        int ButtonSeat, int CurrentSeat, int CurrentBet, int MinRaise,
        int? StateMessageId, long LastActionAt, long CreatedAt)
    {
        public PokerTable ToEntity() => new()
        {
            InviteCode = InviteCode,
            ChatId = ChatId,
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
            StateMessageId = StateMessageId,
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
        "total_committed AS TotalCommitted, has_acted_round AS HasActedThisRound, " +
        "chat_id AS ChatId, state_message_id AS StateMessageId, " +
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

    public async Task<PokerSeat?> FindByUserInTableAsync(long userId, string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<SeatRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM poker_seats WHERE user_id = @userId AND invite_code = @inviteCode LIMIT 1",
            new { userId, inviteCode },
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
                 current_bet, total_committed, has_acted_round, chat_id, state_message_id, joined_at)
            VALUES
                (@InviteCode, @Position, @UserId, @DisplayName, @Stack, @HoleCards, @Status,
                 @CurrentBet, @TotalCommitted, @HasActedThisRound, @ChatId, @StateMessageId, @JoinedAt)
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
                total_committed = @TotalCommitted,
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
        int TotalCommitted, bool HasActedThisRound, long ChatId, int? StateMessageId, long JoinedAt)
    {
        public static SeatRow From(PokerSeat s) => new(
            s.InviteCode, s.Position, s.UserId, s.DisplayName,
            s.Stack, s.HoleCards, (int)s.Status, s.CurrentBet,
            s.TotalCommitted, s.HasActedThisRound, s.ChatId, s.StateMessageId, s.JoinedAt);

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
            TotalCommitted = TotalCommitted,
            HasActedThisRound = HasActedThisRound,
            ChatId = ChatId,
            StateMessageId = StateMessageId,
            JoinedAt = JoinedAt,
        };
    }
}
