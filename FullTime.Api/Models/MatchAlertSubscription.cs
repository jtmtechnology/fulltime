namespace FullTime.Api.Models;

// A single match a user has individually opted into alerts for, independent of whether either team
// or the competition is one of their favourites (see FavouriteTeam/FavouriteLeague) - this is the
// backing state for the bell icon on MatchCard.razor. Cascade-deletes with the match itself (no
// "orphaned subscription" cleanup needed), same default behavior as BetBuilderMarket/
// MatchPlayerStat's FK to Match.
public class MatchAlertSubscription
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public required Guid MatchId { get; set; }
    public Match? Match { get; set; }
    public DateTime CreatedAt { get; set; }
}
