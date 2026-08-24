namespace FullTime.Api.Models;

public class LeagueMembership
{
    public Guid Id { get; set; }
    public Guid LeagueId { get; set; }
    public League? League { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTime JoinedAt { get; set; }
    public decimal Balance { get; set; }

    // Snapshotted from BettingOptions.StartingBalance at the moment this membership was created -
    // deliberately NOT re-read from live config when computing Profit (Balance - StartingBalance),
    // since that config value can change later (confirmed happening: dropping it from 1000 to 100
    // retroactively inflated every existing member's displayed profit by 900, and turned at least
    // one real loss into a fake profit). Each member's own baseline stays fixed at whatever it
    // actually was when they joined, regardless of what StartingBalance is set to afterwards.
    public decimal StartingBalance { get; set; }
}
