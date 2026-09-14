using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<OddsSnapshot> OddsSnapshots => Set<OddsSnapshot>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<BetLeg> BetLegs => Set<BetLeg>();
    public DbSet<BetLegPick> BetLegPicks => Set<BetLegPick>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMembership> LeagueMemberships => Set<LeagueMembership>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<BetBuilderMarket> BetBuilderMarkets => Set<BetBuilderMarket>();
    public DbSet<MatchPlayerStat> MatchPlayerStats => Set<MatchPlayerStat>();
    public DbSet<MatchEvent> MatchEvents => Set<MatchEvent>();
    public DbSet<BetBuilderBoost> BetBuilderBoosts => Set<BetBuilderBoost>();
    public DbSet<UserAlertPreferences> UserAlertPreferences => Set<UserAlertPreferences>();
    public DbSet<FavouriteTeam> FavouriteTeams => Set<FavouriteTeam>();
    public DbSet<FavouriteLeague> FavouriteLeagues => Set<FavouriteLeague>();
    public DbSet<MatchAlertSubscription> MatchAlertSubscriptions => Set<MatchAlertSubscription>();
    public DbSet<SentMatchAlert> SentMatchAlerts => Set<SentMatchAlert>();

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

        modelBuilder.Entity<BetLeg>()
            .HasOne(l => l.Bet)
            .WithMany(b => b.Legs)
            .HasForeignKey(l => l.BetId);

        modelBuilder.Entity<BetLeg>()
            .HasOne(l => l.Match)
            .WithMany(m => m.BetLegs)
            .HasForeignKey(l => l.MatchId);

        modelBuilder.Entity<BetLegPick>()
            .HasOne(p => p.BetLeg)
            .WithMany(l => l.Picks)
            .HasForeignKey(p => p.BetLegId);

        modelBuilder.Entity<User>()
            .Property(u => u.Balance)
            .HasPrecision(18, 2);

        modelBuilder.Entity<User>()
            .Property(u => u.PendingBoostMultiplier)
            .HasPrecision(5, 2);

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

        modelBuilder.Entity<BetLeg>()
            .Property(l => l.OddsAtPlacement).HasPrecision(10, 2);

        modelBuilder.Entity<BetLegPick>()
            .Property(p => p.OddsAtPlacement).HasPrecision(10, 2);

        modelBuilder.Entity<BetLegPick>()
            .Property(p => p.Line).HasPrecision(5, 2);

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

        modelBuilder.Entity<DeviceToken>()
            .HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId);

        modelBuilder.Entity<DeviceToken>()
            .HasIndex(d => d.Token)
            .IsUnique();

        modelBuilder.Entity<BetBuilderMarket>()
            .HasOne(m => m.Match)
            .WithMany(match => match.BetBuilderMarkets)
            .HasForeignKey(m => m.MatchId);

        modelBuilder.Entity<BetBuilderMarket>()
            .Property(m => m.Line).HasPrecision(5, 2);
        modelBuilder.Entity<BetBuilderMarket>()
            .Property(m => m.Price).HasPrecision(10, 2);

        modelBuilder.Entity<MatchPlayerStat>()
            .HasOne(s => s.Match)
            .WithMany(m => m.PlayerStats)
            .HasForeignKey(s => s.MatchId);

        modelBuilder.Entity<BetBuilderBoost>()
            .HasOne(b => b.Match)
            .WithMany()
            .HasForeignKey(b => b.MatchId);

        modelBuilder.Entity<BetBuilderBoost>()
            .HasIndex(b => b.Date)
            .IsUnique();

        modelBuilder.Entity<UserAlertPreferences>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId);

        modelBuilder.Entity<UserAlertPreferences>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        modelBuilder.Entity<FavouriteTeam>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId);

        modelBuilder.Entity<FavouriteTeam>()
            .HasIndex(f => new { f.UserId, f.TeamId })
            .IsUnique();

        modelBuilder.Entity<FavouriteLeague>()
            .HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId);

        modelBuilder.Entity<FavouriteLeague>()
            .HasIndex(f => new { f.UserId, f.LeagueId })
            .IsUnique();

        modelBuilder.Entity<MatchAlertSubscription>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId);

        modelBuilder.Entity<MatchAlertSubscription>()
            .HasOne(s => s.Match)
            .WithMany()
            .HasForeignKey(s => s.MatchId);

        modelBuilder.Entity<MatchAlertSubscription>()
            .HasIndex(s => new { s.UserId, s.MatchId })
            .IsUnique();

        modelBuilder.Entity<SentMatchAlert>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId);

        modelBuilder.Entity<SentMatchAlert>()
            .HasOne(s => s.Match)
            .WithMany()
            .HasForeignKey(s => s.MatchId);

        modelBuilder.Entity<SentMatchAlert>()
            .HasIndex(s => new { s.UserId, s.MatchId, s.AlertType, s.Sequence })
            .IsUnique();
    }
}
