using BotFramework.Host;
using Dapper;

namespace Games.SecretHitler;

public interface ISecretHitlerGameStore
{
    Task<SecretHitlerGame?> FindAsync(string inviteCode, CancellationToken ct);
    Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct);
    Task InsertAsync(SecretHitlerGame game, CancellationToken ct);
    Task UpdateAsync(SecretHitlerGame game, CancellationToken ct);
}

public interface ISecretHitlerPlayerStore
{
    Task<SecretHitlerPlayer?> FindByUserAsync(long userId, CancellationToken ct);
    Task<List<SecretHitlerPlayer>> ListByGameAsync(string inviteCode, CancellationToken ct);
    Task<bool> AnyForUserAsync(long userId, CancellationToken ct);
    Task<int> CountByGameAsync(string inviteCode, CancellationToken ct);
    Task InsertAsync(SecretHitlerPlayer player, CancellationToken ct);
    Task UpdateAsync(SecretHitlerPlayer player, CancellationToken ct);
    Task DeleteAsync(string inviteCode, int position, CancellationToken ct);
    Task UpsertStateMessageAsync(long userId, int messageId, CancellationToken ct);
}

public sealed class SecretHitlerGameStore(INpgsqlConnectionFactory connections) : ISecretHitlerGameStore
{
    private const string SelectColumns =
        "invite_code AS InviteCode, host_user_id AS HostUserId, chat_id AS ChatId, " +
        "status AS Status, phase AS Phase, liberal_policies AS LiberalPolicies, " +
        "fascist_policies AS FascistPolicies, election_tracker AS ElectionTracker, " +
        "current_president_position AS CurrentPresidentPosition, " +
        "nominated_chancellor_position AS NominatedChancellorPosition, " +
        "last_elected_president_position AS LastElectedPresidentPosition, " +
        "last_elected_chancellor_position AS LastElectedChancellorPosition, " +
        "deck_state AS DeckState, discard_state AS DiscardState, " +
        "president_draw AS PresidentDraw, chancellor_received AS ChancellorReceived, " +
        "winner AS Winner, win_reason AS WinReason, " +
        "buy_in AS BuyIn, pot AS Pot, created_at AS CreatedAt, last_action_at AS LastActionAt";

    public async Task<SecretHitlerGame?> FindAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<GameRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM secret_hitler_games WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<bool> CodeExistsAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM secret_hitler_games WHERE invite_code = @inviteCode)",
            new { inviteCode },
            cancellationToken: ct));
    }

    public async Task InsertAsync(SecretHitlerGame g, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO secret_hitler_games
                (invite_code, host_user_id, chat_id, status, phase,
                 liberal_policies, fascist_policies, election_tracker,
                 current_president_position, nominated_chancellor_position,
                 last_elected_president_position, last_elected_chancellor_position,
                 deck_state, discard_state, president_draw, chancellor_received,
                 winner, win_reason, buy_in, pot, created_at, last_action_at)
            VALUES
                (@InviteCode, @HostUserId, @ChatId, @Status, @Phase,
                 @LiberalPolicies, @FascistPolicies, @ElectionTracker,
                 @CurrentPresidentPosition, @NominatedChancellorPosition,
                 @LastElectedPresidentPosition, @LastElectedChancellorPosition,
                 @DeckState, @DiscardState, @PresidentDraw, @ChancellorReceived,
                 @Winner, @WinReason, @BuyIn, @Pot, @CreatedAt, @LastActionAt)
            """,
            GameRow.From(g),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(SecretHitlerGame g, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE secret_hitler_games SET
                host_user_id = @HostUserId,
                chat_id = @ChatId,
                status = @Status,
                phase = @Phase,
                liberal_policies = @LiberalPolicies,
                fascist_policies = @FascistPolicies,
                election_tracker = @ElectionTracker,
                current_president_position = @CurrentPresidentPosition,
                nominated_chancellor_position = @NominatedChancellorPosition,
                last_elected_president_position = @LastElectedPresidentPosition,
                last_elected_chancellor_position = @LastElectedChancellorPosition,
                deck_state = @DeckState,
                discard_state = @DiscardState,
                president_draw = @PresidentDraw,
                chancellor_received = @ChancellorReceived,
                winner = @Winner,
                win_reason = @WinReason,
                buy_in = @BuyIn,
                pot = @Pot,
                last_action_at = @LastActionAt
            WHERE invite_code = @InviteCode
            """,
            GameRow.From(g),
            cancellationToken: ct));
    }

    private sealed record GameRow(
        string InviteCode, long HostUserId, long ChatId, int Status, int Phase,
        int LiberalPolicies, int FascistPolicies, int ElectionTracker,
        int CurrentPresidentPosition, int NominatedChancellorPosition,
        int LastElectedPresidentPosition, int LastElectedChancellorPosition,
        string DeckState, string DiscardState, string PresidentDraw, string ChancellorReceived,
        int Winner, int WinReason, int BuyIn, int Pot, long CreatedAt, long LastActionAt)
    {
        public static GameRow From(SecretHitlerGame g) => new(
            g.InviteCode, g.HostUserId, g.ChatId, (int)g.Status, (int)g.Phase,
            g.LiberalPolicies, g.FascistPolicies, g.ElectionTracker,
            g.CurrentPresidentPosition, g.NominatedChancellorPosition,
            g.LastElectedPresidentPosition, g.LastElectedChancellorPosition,
            g.DeckState, g.DiscardState, g.PresidentDraw, g.ChancellorReceived,
            (int)g.Winner, (int)g.WinReason, g.BuyIn, g.Pot, g.CreatedAt, g.LastActionAt);

        public SecretHitlerGame ToEntity() => new()
        {
            InviteCode = InviteCode,
            HostUserId = HostUserId,
            ChatId = ChatId,
            Status = (ShStatus)Status,
            Phase = (ShPhase)Phase,
            LiberalPolicies = LiberalPolicies,
            FascistPolicies = FascistPolicies,
            ElectionTracker = ElectionTracker,
            CurrentPresidentPosition = CurrentPresidentPosition,
            NominatedChancellorPosition = NominatedChancellorPosition,
            LastElectedPresidentPosition = LastElectedPresidentPosition,
            LastElectedChancellorPosition = LastElectedChancellorPosition,
            DeckState = DeckState,
            DiscardState = DiscardState,
            PresidentDraw = PresidentDraw,
            ChancellorReceived = ChancellorReceived,
            Winner = (ShWinner)Winner,
            WinReason = (ShWinReason)WinReason,
            BuyIn = BuyIn,
            Pot = Pot,
            CreatedAt = CreatedAt,
            LastActionAt = LastActionAt,
        };
    }
}

public sealed class SecretHitlerPlayerStore(INpgsqlConnectionFactory connections) : ISecretHitlerPlayerStore
{
    private const string SelectColumns =
        "invite_code AS InviteCode, position AS Position, user_id AS UserId, " +
        "display_name AS DisplayName, chat_id AS ChatId, role AS Role, " +
        "is_alive AS IsAlive, last_vote AS LastVote, " +
        "state_message_id AS StateMessageId, joined_at AS JoinedAt";

    public async Task<SecretHitlerPlayer?> FindByUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<PlayerRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM secret_hitler_players WHERE user_id = @userId LIMIT 1",
            new { userId },
            cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<List<SecretHitlerPlayer>> ListByGameAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        var rows = await conn.QueryAsync<PlayerRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM secret_hitler_players WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
        return rows.Select(r => r.ToEntity()).ToList();
    }

    public async Task<bool> AnyForUserAsync(long userId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM secret_hitler_players WHERE user_id = @userId)",
            new { userId },
            cancellationToken: ct));
    }

    public async Task<int> CountByGameAsync(string inviteCode, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM secret_hitler_players WHERE invite_code = @inviteCode",
            new { inviteCode },
            cancellationToken: ct));
    }

    public async Task InsertAsync(SecretHitlerPlayer p, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO secret_hitler_players
                (invite_code, position, user_id, display_name, chat_id, role,
                 is_alive, last_vote, state_message_id, joined_at)
            VALUES
                (@InviteCode, @Position, @UserId, @DisplayName, @ChatId, @Role,
                 @IsAlive, @LastVote, @StateMessageId, @JoinedAt)
            """,
            PlayerRow.From(p),
            cancellationToken: ct));
    }

    public async Task UpdateAsync(SecretHitlerPlayer p, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE secret_hitler_players SET
                user_id = @UserId,
                display_name = @DisplayName,
                chat_id = @ChatId,
                role = @Role,
                is_alive = @IsAlive,
                last_vote = @LastVote,
                state_message_id = @StateMessageId
            WHERE invite_code = @InviteCode AND position = @Position
            """,
            PlayerRow.From(p),
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string inviteCode, int position, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM secret_hitler_players WHERE invite_code = @inviteCode AND position = @position",
            new { inviteCode, position },
            cancellationToken: ct));
    }

    public async Task UpsertStateMessageAsync(long userId, int messageId, CancellationToken ct)
    {
        await using var conn = await connections.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE secret_hitler_players SET state_message_id = @messageId WHERE user_id = @userId",
            new { userId, messageId },
            cancellationToken: ct));
    }

    private sealed record PlayerRow(
        string InviteCode, int Position, long UserId, string DisplayName, long ChatId,
        int Role, bool IsAlive, int LastVote, int? StateMessageId, long JoinedAt)
    {
        public static PlayerRow From(SecretHitlerPlayer p) => new(
            p.InviteCode, p.Position, p.UserId, p.DisplayName, p.ChatId,
            (int)p.Role, p.IsAlive, (int)p.LastVote, p.StateMessageId, p.JoinedAt);

        public SecretHitlerPlayer ToEntity() => new()
        {
            InviteCode = InviteCode,
            Position = Position,
            UserId = UserId,
            DisplayName = DisplayName,
            ChatId = ChatId,
            Role = (ShRole)Role,
            IsAlive = IsAlive,
            LastVote = (ShVote)LastVote,
            StateMessageId = StateMessageId,
            JoinedAt = JoinedAt,
        };
    }
}
