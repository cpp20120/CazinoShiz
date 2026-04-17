using CasinoShiz.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CasinoShiz.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserState> Users => Set<UserState>();
    public DbSet<ChatRegistration> Chats => Set<ChatRegistration>();
    public DbSet<HorseBet> HorseBets => Set<HorseBet>();
    public DbSet<HorseResult> HorseResults => Set<HorseResult>();
    public DbSet<FreespinCode> FreespinCodes => Set<FreespinCode>();
    public DbSet<DisplayNameOverride> DisplayNameOverrides => Set<DisplayNameOverride>();
    public DbSet<PokerTable> PokerTables => Set<PokerTable>();
    public DbSet<PokerSeat> PokerSeats => Set<PokerSeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HorseBet>()
            .HasIndex(b => new { b.RaceDate, b.UserId });

        modelBuilder.Entity<FreespinCode>()
            .HasIndex(c => c.Active);

        modelBuilder.Entity<PokerSeat>()
            .HasKey(s => new { s.InviteCode, s.Position });

        modelBuilder.Entity<PokerSeat>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<PokerTable>()
            .HasIndex(t => t.Status);
    }
}
