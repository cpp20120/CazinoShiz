using BotFramework.Host.Persistence.Connections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Games.SecretHitler.Infrastructure.Persistence;

public sealed class SecretHitlerDbContext(INpgsqlConnectionFactory connections) : DbContext
{
    public DbSet<SecretHitlerGame> Games => Set<SecretHitlerGame>();
    public DbSet<SecretHitlerPlayer> Players => Set<SecretHitlerPlayer>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder
                .UseNpgsql(connections.Create().ConnectionString)
                .AddInterceptors(TenantDatabaseConnectionInterceptor.Instance);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureGame(modelBuilder.Entity<SecretHitlerGame>());
        ConfigurePlayer(modelBuilder.Entity<SecretHitlerPlayer>());
    }

    private static void ConfigureGame(EntityTypeBuilder<SecretHitlerGame> game)
    {
        game.ToTable("secret_hitler_games");
        game.HasKey(x => x.InviteCode);
        game.Property(x => x.InviteCode).HasColumnName("invite_code");
        game.Property(x => x.HostUserId).HasColumnName("host_user_id");
        game.Property(x => x.ChatId).HasColumnName("chat_id");
        game.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        game.Property(x => x.Phase).HasColumnName("phase").HasConversion<int>();
        game.Property(x => x.LiberalPolicies).HasColumnName("liberal_policies");
        game.Property(x => x.FascistPolicies).HasColumnName("fascist_policies");
        game.Property(x => x.ElectionTracker).HasColumnName("election_tracker");
        game.Property(x => x.CurrentPresidentPosition).HasColumnName("current_president_position");
        game.Property(x => x.NominatedChancellorPosition).HasColumnName("nominated_chancellor_position");
        game.Property(x => x.LastElectedPresidentPosition).HasColumnName("last_elected_president_position");
        game.Property(x => x.LastElectedChancellorPosition).HasColumnName("last_elected_chancellor_position");
        game.Property(x => x.DeckState).HasColumnName("deck_state");
        game.Property(x => x.DiscardState).HasColumnName("discard_state");
        game.Property(x => x.PresidentDraw).HasColumnName("president_draw");
        game.Property(x => x.ChancellorReceived).HasColumnName("chancellor_received");
        game.Property(x => x.Winner).HasColumnName("winner").HasConversion<int>();
        game.Property(x => x.WinReason).HasColumnName("win_reason").HasConversion<int>();
        game.Property(x => x.BuyIn).HasColumnName("buy_in");
        game.Property(x => x.Pot).HasColumnName("pot");
        game.Property(x => x.StateMessageId).HasColumnName("state_message_id");
        game.Property(x => x.CreatedAt).HasColumnName("created_at");
        game.Property(x => x.LastActionAt).HasColumnName("last_action_at");
        game.HasIndex(x => new { x.Status, x.LastActionAt }).HasDatabaseName("ix_sh_games_status_action");
    }

    private static void ConfigurePlayer(EntityTypeBuilder<SecretHitlerPlayer> player)
    {
        player.ToTable("secret_hitler_players");
        player.HasKey(x => new { x.InviteCode, x.Position });
        player.Property(x => x.InviteCode).HasColumnName("invite_code");
        player.Property(x => x.Position).HasColumnName("position");
        player.Property(x => x.UserId).HasColumnName("user_id");
        player.Property(x => x.DisplayName).HasColumnName("display_name");
        player.Property(x => x.ChatId).HasColumnName("chat_id");
        player.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
        player.Property(x => x.IsAlive).HasColumnName("is_alive");
        player.Property(x => x.LastVote).HasColumnName("last_vote").HasConversion<int>();
        player.Property(x => x.StateMessageId).HasColumnName("state_message_id");
        player.Property(x => x.JoinedAt).HasColumnName("joined_at");
        player.Property(x => x.WagerBetId).HasColumnName("wager_bet_id");
        player.HasIndex(x => x.UserId).HasDatabaseName("ix_sh_players_user");
        player.HasIndex(x => x.InviteCode).HasDatabaseName("ix_sh_players_code");
    }
}
