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

    public List<BetLeg> Legs { get; set; } = [];
}
