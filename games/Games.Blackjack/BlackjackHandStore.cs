using BotFramework.Host;
using BotFramework.Sdk;
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

public sealed class BlackjackHandStore(
    INpgsqlConnectionFactory connections,
    IEventStore events,
    IEventSerializer serializer) : IBlackjackHandStore
{
    public async Task<BlackjackHandRow?> FindAsync(long userId, CancellationToken ct)
    {
        var state = await LoadStateAsync(userId, ct);
        if (state.Active is not null)
            return state.Active;

        return await FindLegacyProjectionAsync(userId, ct);
    }

    public async Task<bool> InsertAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        var state = await LoadStateAsync(hand.UserId, ct);
        if (state.Active is not null)
            return false;

        var ev = new BlackjackHandStarted(
            hand.UserId,
            hand.ChatId,
            hand.Bet,
            hand.PlayerCards,
            hand.DealerCards,
            hand.DeckState,
            hand.StateMessageId,
            hand.CreatedAt.ToUnixTimeMilliseconds(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await events.AppendAsync(StreamId(hand.UserId), state.Version, [ev], ct);
        await UpsertProjectionAsync(hand, ct);
        return true;
    }

    public async Task UpdateAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        var (expectedVersion, prefix) = await EnsureStreamContainsActiveHandAsync(hand, ct);
        var ev = new BlackjackHandUpdated(
            hand.UserId,
            hand.ChatId,
            hand.Bet,
            hand.PlayerCards,
            hand.DealerCards,
            hand.DeckState,
            hand.StateMessageId,
            hand.CreatedAt.ToUnixTimeMilliseconds(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await events.AppendAsync(StreamId(hand.UserId), expectedVersion, [.. prefix, ev], ct);
        await UpsertProjectionAsync(hand, ct);
    }

    public async Task DeleteAsync(long userId, CancellationToken ct)
    {
        var state = await LoadStateAsync(userId, ct);
        BlackjackHandRow? active = state.Active;
        var expectedVersion = state.Version;
        var prefix = new List<IDomainEvent>();

        if (active is null)
        {
            active = await FindLegacyProjectionAsync(userId, ct);
            if (active is null)
            {
                await DeleteProjectionAsync(userId, ct);
                return;
            }

            prefix.Add(ToStartedEvent(active));
        }

        var ev = new BlackjackHandClosed(
            active.UserId,
            active.ChatId,
            active.CreatedAt.ToUnixTimeMilliseconds(),
            "settled",
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await events.AppendAsync(StreamId(userId), expectedVersion, [.. prefix, ev], ct);
        await DeleteProjectionAsync(userId, ct);
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
        var state = await LoadStateAsync(userId, ct);
        BlackjackHandRow? active = state.Active;
        var expectedVersion = state.Version;
        var prefix = new List<IDomainEvent>();

        if (active is null)
        {
            active = await FindLegacyProjectionAsync(userId, ct);
            if (active is null)
                return;
            prefix.Add(ToStartedEvent(active));
        }

        var ev = new BlackjackStateMessageSet(userId, messageId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await events.AppendAsync(StreamId(userId), expectedVersion, [.. prefix, ev], ct);
        await SetProjectionStateMessageIdAsync(userId, messageId, ct);
    }

    private async Task<(long ExpectedVersion, List<IDomainEvent> Prefix)> EnsureStreamContainsActiveHandAsync(
        BlackjackHandRow hand,
        CancellationToken ct)
    {
        var state = await LoadStateAsync(hand.UserId, ct);
        if (state.Active is not null)
            return (state.Version, []);

        var legacy = await FindLegacyProjectionAsync(hand.UserId, ct);
        if (legacy is null)
            return (state.Version, []);

        return (state.Version, [ToStartedEvent(legacy)]);
    }

    private async Task<BlackjackEsState> LoadStateAsync(long userId, CancellationToken ct)
    {
        var stream = await events.LoadAsync(StreamId(userId), ct);
        BlackjackHandRow? active = null;
        long version = 0;

        foreach (var stored in stream)
        {
            version = stored.Version;
            var ev = serializer.Deserialize(stored.EventType, stored.PayloadJson);
            switch (ev)
            {
                case BlackjackHandStarted started:
                    active = FromStarted(started);
                    break;
                case BlackjackHandUpdated updated:
                    active = FromUpdated(updated);
                    break;
                case BlackjackStateMessageSet stateMessage when active is not null:
                    active = active with { StateMessageId = stateMessage.StateMessageId };
                    break;
                case BlackjackHandClosed:
                    active = null;
                    break;
            }
        }

        return new BlackjackEsState(version, active);
    }

    private async Task<BlackjackHandRow?> FindLegacyProjectionAsync(long userId, CancellationToken ct)
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

    private async Task UpsertProjectionAsync(BlackjackHandRow hand, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO blackjack_hands
                (user_id, chat_id, bet, player_cards, dealer_cards, deck_state, state_message_id, created_at)
            VALUES (@UserId, @ChatId, @Bet, @PlayerCards, @DealerCards, @DeckState, @StateMessageId, @CreatedAt)
            ON CONFLICT (user_id) DO UPDATE SET
                chat_id = EXCLUDED.chat_id,
                bet = EXCLUDED.bet,
                player_cards = EXCLUDED.player_cards,
                dealer_cards = EXCLUDED.dealer_cards,
                deck_state = EXCLUDED.deck_state,
                state_message_id = EXCLUDED.state_message_id,
                created_at = EXCLUDED.created_at
            """,
            hand,
            cancellationToken: ct));
    }

    private async Task DeleteProjectionAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM blackjack_hands WHERE user_id = @userId",
            new { userId },
            cancellationToken: ct));
    }

    private async Task SetProjectionStateMessageIdAsync(long userId, int messageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE blackjack_hands SET state_message_id = @messageId WHERE user_id = @userId",
            new { userId, messageId },
            cancellationToken: ct));
    }

    private static BlackjackHandStarted ToStartedEvent(BlackjackHandRow hand) =>
        new(
            hand.UserId,
            hand.ChatId,
            hand.Bet,
            hand.PlayerCards,
            hand.DealerCards,
            hand.DeckState,
            hand.StateMessageId,
            hand.CreatedAt.ToUnixTimeMilliseconds(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static BlackjackHandRow FromStarted(BlackjackHandStarted ev) =>
        new(
            ev.UserId,
            ev.ChatId,
            ev.Bet,
            ev.PlayerCards,
            ev.DealerCards,
            ev.DeckState,
            ev.StateMessageId,
            DateTimeOffset.FromUnixTimeMilliseconds(ev.CreatedAtMs));

    private static BlackjackHandRow FromUpdated(BlackjackHandUpdated ev) =>
        new(
            ev.UserId,
            ev.ChatId,
            ev.Bet,
            ev.PlayerCards,
            ev.DealerCards,
            ev.DeckState,
            ev.StateMessageId,
            DateTimeOffset.FromUnixTimeMilliseconds(ev.CreatedAtMs));

    private static string StreamId(long userId) => $"blackjack:{userId}";

    private sealed record BlackjackEsState(long Version, BlackjackHandRow? Active);
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