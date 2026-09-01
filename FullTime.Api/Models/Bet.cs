namespace FullTime.Api.Models;

public class Bet
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // null = the global "Worldwide" pool; non-null = a specific private league's own balance.
    public Guid? LeagueId { get; set; }
    public League? League { get; set; }

    public decimal Stake { get; set; }
    public decimal CombinedOdds { get; set; }
    public decimal PotentialReturn { get; set; }
    public BetStatus Status { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTime? SettledAt { get; set; }

    // Set when a pending Daily Spinner boost (see SpinService) was consumed by this bet - already
    // folded into CombinedOdds/PotentialReturn above, this is purely a label for display (e.g. "Bet
    // Boost 25%") so My Bets can show that a boost applied here.
    public string? BoostApplied { get; set; }

    public List<BetLeg> Legs { get; set; } = [];
}
