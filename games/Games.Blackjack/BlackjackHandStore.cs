using BotFramework.Host;
using Dapper;

namespace Games.Blackjack;

public interface IBlackjackHandStore
{
    Task<BlackjackHandRow?> FindAsync(long userId, CancellationToken ct);
    Task<bool> InsertAsync(BlackjackHandRow hand, CancellationToken ct);
    Task UpdateAsync(BlackjackHandRow hand, CancellationToken ct);
    Task DeleteAsync(long userId, CancellationToken ct);
    Task<IReadOnlyList<long>> ListStuckUserIdsAsync(DateTimeOffset cutoff, CancellationToken ct);
    Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct);
}

public sealed class BlackjackHandStore(INpgsqlConnectionFactory connections) : IBlackjackHandStore
{
    public async Task<BlackjackHandRow?> FindAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<BlackjackHandRow>(new CommandDefinition("""
            SELECT user_id AS UserId, chat_id AS ChatId, bet AS Bet,
                   player_cards AS PlayerCards, dealer_cards AS DealerCards,
                   deck_state AS DeckState, state_message_id AS StateMessageId,
                   created_at AS CreatedAt
            FROM blackjack_hands WHERE user_id = @userId
            """,
            new { userId },
            cancellationToken: ct));
    }

    public async Task<bool> InsertAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO blackjack_hands
                (user_id, chat_id, bet, player_cards, dealer_cards, deck_state, state_message_id, created_at)
            VALUES (@UserId, @ChatId, @Bet, @PlayerCards, @DealerCards, @DeckState, @StateMessageId, @CreatedAt)
            ON CONFLICT (user_id) DO NOTHING
            """,
            hand,
            cancellationToken: ct));
        return rows > 0;
    }

    public async Task UpdateAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE blackjack_hands SET
                bet = @Bet,
                player_cards = @PlayerCards,
                dealer_cards = @DealerCards,
                deck_state = @DeckState,
                state_message_id = @StateMessageId
            WHERE user_id = @UserId
            """,
            hand,
            cancellationToken: ct));
    }

    public async Task DeleteAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM blackjack_hands WHERE user_id = @userId",
            new { userId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<long>> ListStuckUserIdsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var ids = await conn.QueryAsync<long>(new CommandDefinition(
            "SELECT user_id FROM blackjack_hands WHERE created_at < @cutoff",
            new { cutoff },
            cancellationToken: ct));
        return [.. ids];
    }

    public async Task SetStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE blackjack_hands SET state_message_id = @messageId WHERE user_id = @userId",
            new { userId, messageId },
            cancellationToken: ct));
    }
}

public sealed record BlackjackHandRow(
    long UserId,
    long ChatId,
    int Bet,
    string PlayerCards,
    string DealerCards,
    string DeckState,
    int? StateMessageId,
    DateTimeOffset CreatedAt);
