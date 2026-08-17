using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<OddsSnapshot> OddsSnapshots => Set<OddsSnapshot>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<BetSelection> BetSelections => Set<BetSelection>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Match>()
            .HasIndex(m => m.ExternalId)
            .IsUnique();

        modelBuilder.Entity<OddsSnapshot>()
            .HasOne(o => o.Match)
            .WithMany(m => m.OddsSnapshots)
            .HasForeignKey(o => o.MatchId);

        modelBuilder.Entity<Bet>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId);

        modelBuilder.Entity<BetSelection>()
            .HasOne(bs => bs.Bet)
            .WithMany(b => b.Selections)
            .HasForeignKey(bs => bs.BetId);

        modelBuilder.Entity<BetSelection>()
            .HasOne(bs => bs.Match)
            .WithMany(m => m.BetSelections)
            .HasForeignKey(bs => bs.MatchId);

        modelBuilder.Entity<User>()
            .Property(u => u.Balance)
            .HasPrecision(18, 2);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<OddsSnapshot>()
            .Property(o => o.HomeOdds).HasPrecision(10, 2);
        modelBuilder.Entity<OddsSnapshot>()
            .Property(o => o.DrawOdds).HasPrecision(10, 2);
        modelBuilder.Entity<OddsSnapshot>()
            .Property(o => o.AwayOdds).HasPrecision(10, 2);

        modelBuilder.Entity<Bet>()
            .Property(b => b.Stake).HasPrecision(18, 2);
        modelBuilder.Entity<Bet>()
            .Property(b => b.CombinedOdds).HasPrecision(10, 2);
        modelBuilder.Entity<Bet>()
            .Property(b => b.PotentialReturn).HasPrecision(18, 2);

        modelBuilder.Entity<BetSelection>()
            .Property(bs => bs.OddsAtPlacement).HasPrecision(10, 2);

        modelBuilder.Entity<League>()
            .HasOne(l => l.CreatedBy)
            .WithMany()
            .HasForeignKey(l => l.CreatedByUserId);

        modelBuilder.Entity<League>()
            .HasIndex(l => l.InviteCode)
            .IsUnique();

        modelBuilder.Entity<LeagueMembership>()
            .HasOne(lm => lm.League)
            .WithMany(l => l.Memberships)
            .HasForeignKey(lm => lm.LeagueId);

        modelBuilder.Entity<LeagueMembership>()
            .HasOne(lm => lm.User)
            .WithMany()
            .HasForeignKey(lm => lm.UserId);

        modelBuilder.Entity<LeagueMembership>()
            .HasIndex(lm => new { lm.LeagueId, lm.UserId })
            .IsUnique();

        modelBuilder.Entity<LeagueMembership>()
            .Property(lm => lm.Balance).HasPrecision(18, 2);

        // Restrict (not the EF default Cascade, and not SetNull): deleting a league must never
        // erase a bet's history, and reclassifying a league bet as Worldwide would misattribute
        // money that never touched User.Balance. There's no "delete league" feature yet — when one
        // exists it needs an explicit archival design, not a delete that silently mangles this FK.
        modelBuilder.Entity<Bet>()
            .HasOne(b => b.League)
            .WithMany()
            .HasForeignKey(b => b.LeagueId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
