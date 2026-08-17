namespace FullTime.Api.Models;

public class OddsSnapshot
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Match? Match { get; set; }

    public decimal HomeOdds { get; set; }
    public decimal DrawOdds { get; set; }
    public decimal AwayOdds { get; set; }
    public DateTime FetchedAt { get; set; }
    public required string Bookmaker { get; set; }
    public string? BookmakerLogoUrl { get; set; }
}
