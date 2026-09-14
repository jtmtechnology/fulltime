namespace FullTime.Api.Models;

// A team a user wants match alerts for, regardless of competition - TeamId is API-Football's own
// team ID, the same space as Match.HomeTeamId/AwayTeamId (see MatchAlertService, which matches on
// either side). Deliberately no snapshot of the team's name/logo here - both are read live off
// whichever Match row is being evaluated, same as everywhere else in this codebase that avoids
// duplicating provider data that can drift (e.g. a club rebrand) out of sync with its source.
public class FavouriteTeam
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public long TeamId { get; set; }
    public DateTime CreatedAt { get; set; }
}
