using BotFramework.Host.Composition.Migrations;
using BotFramework.Narrative.Host;
using ChatAdministration.Telegram.Infrastructure;
using Dapper;
using Games.Dice.Infrastructure.Migrations;
using Games.DiceCube.Infrastructure.Migrations;
using Games.Blackjack.Infrastructure.Migrations;
using Games.Basketball.Infrastructure.Migrations;
using Games.Bowling.Infrastructure.Migrations;
using Games.Football.Infrastructure.Migrations;
using Games.Pick.Infrastructure.Migrations;
using Games.Darts.Infrastructure.Migrations;
using Games.Horse.Infrastructure.Migrations;
using Games.Meta.Infrastructure.Migrations;
using Games.Poker.Infrastructure.Migrations;
using Games.Challenges.Infrastructure.Migrations;
using Games.Redeem.Infrastructure.Migrations;
using Games.SecretHitler.Infrastructure.Migrations;
using Games.PixelBattle.Infrastructure.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CasinoShiz.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AtomicPostgresCollection : ICollectionFixture<AtomicPostgresFixture>
{
    public const string Name = "AtomicPostgres";
}

public sealed class AtomicPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("casinoshiz_atomic_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        foreach (var migration in new FrameworkMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new NarrativeProjectionMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new DiceMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new DiceCubeMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new BlackjackMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new BasketballMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new BowlingMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new FootballMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new PickMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new DartsMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new HorseMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new PokerMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new ChallengeMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new RedeemMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new SecretHitlerMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new PixelBattleMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new MetaMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in new ChatAdministrationMigrations().Migrations)
            await connection.ExecuteAsync(migration.Sql);
        foreach (var migration in FrameworkMigrations.PostModuleMigrations)
            await connection.ExecuteAsync(migration.Sql);
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            TRUNCATE TABLE
                narrative_projection_deliveries,
                narrative_projections,
                processed_update_inbox,
                integration_inbox_messages,
                integration_outbox_messages,
                integration_message_quarantine,
                durable_workflow_timeouts,
                chat_admin_settings_callbacks,
                chat_admin_audit_events,
                chat_admin_effect_outbox,
                chat_admin_domain_events,
                chat_admin_case_history,
                chat_admin_appeals,
                chat_admin_cases,
                chat_admin_message_index,
                chat_admin_verifications,
                chat_admin_warnings,
                chat_admin_members,
                chat_admin_commands,
                chat_admin_chats,
                tenant_event_outbox,
                tenant_schedule_outbox,
                tenant_aggregate_states,
                game_event_outbox,
                game_schedule_outbox,
                game_execution_history,
                admin_audit,
                game_command_idempotency,
                game_aggregate_states,
                dice_rolls,
                dicecube_bets,
                basketball_bets,
                bowling_bets,
                football_bets,
                pick_chains,
                pick_streaks,
                pick_daily_lottery_tickets,
                pick_daily_lottery,
                pick_lottery_entries,
                pick_lottery,
                darts_rounds,
                horse_bets,
                horse_results,
                poker_seats,
                poker_tables,
                challenge_duels,
                redeem_codes,
                secret_hitler_players,
                secret_hitler_games,
                pixelbattle_tiles,
                economics_ledger,
                wallet_operations,
                telegram_dice_daily_rolls,
                mini_game_sessions,
                users,
                player_protection,
                game_availability_overrides,
                meta_seasons,
                tenant_wallet_ledger,
                tenant_wallets,
                blackjack_hands
            RESTART IDENTITY CASCADE
            """);
        await connection.ExecuteAsync(
            "UPDATE runtime_tuning SET payload = '{}'::jsonb, updated_at = now() WHERE id = 1");
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var result = await connection.ExecuteScalarAsync<T>(sql, parameters);
        return result is null ? throw new InvalidOperationException("Scalar query returned null.") : result;
    }
}
