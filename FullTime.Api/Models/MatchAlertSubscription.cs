namespace FullTime.Api.Models;

// A single match a user has explicitly overridden alerts for - the backing state for the bell icon
// on MatchCard.razor, and the one place a match's alert status can be forced either way regardless
// of FavouriteTeam/FavouriteLeague. Included=true forces alerts on even with no matching favourite;
// Included=false forces them off even when the match DOES involve a favourited team/league - without
// this override, unfavouriting the team would be the only way to silence one specific match, and
// there'd be no way to opt into just one game without favouriting its whole team. See
// MatchAlertService's interested-users query, the only place this table is actually read against
// FavouriteTeam/FavouriteLeague. Cascade-deletes with the match itself, same default behavior as
// BetBuilderMarket/MatchPlayerStat's FK to Match.
public class MatchAlertSubscription
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public required Guid MatchId { get; set; }
    public Match? Match { get; set; }
    public bool Included { get; set; }
    public DateTime CreatedAt { get; set; }
}
