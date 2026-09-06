namespace FullTime.Api.Sandbox.Models;

// Deliberately minimal - just enough to prove the upsert/mapping logic against real API-Football
// data before touching production's Match model at all. Source is always "api-football" for now;
// add a discriminator if the-odds-api-sourced rows ever need to live alongside these.
public class SandboxMatch
{
    public Guid Id { get; set; }
    public required string ExternalId { get; set; }
    public long LeagueId { get; set; }
    public required string LeagueName { get; set; }
    public required string HomeTeam { get; set; }
    public required string AwayTeam { get; set; }
    public long HomeTeamId { get; set; }
    public long AwayTeamId { get; set; }
    public string? HomeTeamLogoUrl { get; set; }
    public string? AwayTeamLogoUrl { get; set; }
    public DateTime KickoffTime { get; set; }
    public required string StatusShort { get; set; }
    public int? Elapsed { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
