namespace FullTime.Api.Models;

// A competition a user wants match alerts for, every fixture in it. LeagueId is Highlightly's ID
// space - the same one Match.LeagueId/LeagueCatalog already use regardless of which live provider
// is active (see HighlightlyToApiFootballLeagueMap's own comment for why that's the shared space).
public class FavouriteLeague
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public long LeagueId { get; set; }
    public DateTime CreatedAt { get; set; }
}
