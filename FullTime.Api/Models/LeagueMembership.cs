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
}
