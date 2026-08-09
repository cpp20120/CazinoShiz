// ─────────────────────────────────────────────────────────────────────────────
// PokerStores — Dapper-backed access for poker_tables + poker_seats. The
// domain mutates PokerTable/PokerSeat instances in place, so updates
// overwrite every field from the in-memory object.
//
// Mutations use PokerExecutionStateStore in the executor transaction. This
// store remains the read-side facade used by handlers and jobs.
// ─────────────────────────────────────────────────────────────────────────────

using Dapper;

namespace Games.Poker.Infrastructure.Persistence;

public sealed class PokerSeatStore(INpgsqlConnectionFactory connections) : IPokerSeatStore
{
    private const string SelectColumns =
        "invite_code AS InviteCode, position AS Position, user_id AS UserId, display_name AS DisplayName, " +
        "stack AS Stack, hole_cards AS HoleCards, status AS Status, current_bet AS CurrentBet, " +
        "total_committed AS TotalCommitted, has_acted_round AS HasActedThisRound, " +
        "chat_id AS ChatId, state_message_id AS StateMessageId, " +
        "joined_at AS JoinedAt, wager_bet_id AS WagerBetId";

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

    public async Task<IReadOnlyList<PokerSeat>> ListByTableAsync(string inviteCode, CancellationToken ct)
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

    public async Task InsertAsync(PokerSeat seat, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO poker_seats
                (invite_code, position, user_id, display_name, stack, hole_cards, status,
                 current_bet, total_committed, has_acted_round, chat_id, state_message_id, joined_at,
                 wager_bet_id)
            VALUES
                (@InviteCode, @Position, @UserId, @DisplayName, @Stack, @HoleCards, @Status,
                 @CurrentBet, @TotalCommitted, @HasActedThisRound, @ChatId, @StateMessageId, @JoinedAt,
                 @WagerBetId)
            """,
            SeatRow.From(seat),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(PokerSeat seat, CancellationToken ct)
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
            SeatRow.From(seat),
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
        int TotalCommitted, bool HasActedThisRound, long ChatId, int? StateMessageId, long JoinedAt,
        string? WagerBetId)
    {
        public static SeatRow From(PokerSeat s) => new(
            s.InviteCode, s.Position, s.UserId, s.DisplayName,
            s.Stack, s.HoleCards, (int)s.Status, s.CurrentBet,
            s.TotalCommitted, s.HasActedThisRound, s.ChatId, s.StateMessageId, s.JoinedAt, s.WagerBetId);

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
            WagerBetId = WagerBetId,
        };
    }
}
