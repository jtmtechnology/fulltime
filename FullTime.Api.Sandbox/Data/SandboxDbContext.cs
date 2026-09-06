using FullTime.Api.Sandbox.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Sandbox.Data;

// Points at a brand-new database (friendsacca_sandbox on the VM, see HANDOVER.md), never the live
// friendsacca one - the whole point of this project is testing API-Football/the-odds-api without
// any risk to real users' data.
public class SandboxDbContext(DbContextOptions<SandboxDbContext> options) : DbContext(options)
{
    public DbSet<SandboxMatch> Matches => Set<SandboxMatch>();
    public DbSet<SandboxPlayerPropMarket> PlayerPropMarkets => Set<SandboxPlayerPropMarket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SandboxMatch>()
            .HasIndex(m => m.ExternalId)
            .IsUnique();

        modelBuilder.Entity<SandboxPlayerPropMarket>()
            .HasOne(p => p.Match)
            .WithMany()
            .HasForeignKey(p => p.MatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SandboxPlayerPropMarket>()
            .Property(p => p.Price).HasPrecision(10, 2);
        modelBuilder.Entity<SandboxPlayerPropMarket>()
            .Property(p => p.Point).HasPrecision(5, 2);
    }
}
