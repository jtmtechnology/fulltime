namespace FullTime.Api.Models;

// One value per push-alert type a user can opt into for a match (see UserAlertPreferences,
// MatchAlertService) - also the discriminator column in SentMatchAlert's per-(user, match, type)
// dedup ledger, so each of these can only ever fire once per match per user regardless of how many
// sync ticks notice the same transition.
public enum MatchAlertType
{
    LineupsOut,
    Kickoff,
    HalfTime,
    Goal,
    RedCard,
    FullTime,
    YellowCard
}
