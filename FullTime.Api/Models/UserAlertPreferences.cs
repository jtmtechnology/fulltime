namespace FullTime.Api.Models;

// One row per user, created lazily the first time AlertsController.GetPreferences is called for
// them (most users will never touch this feature) - which of the six alert types they want to
// receive at all, independent of *which* matches (see FavouriteTeam/FavouriteLeague/
// MatchAlertSubscription for that side). All default false: this is a brand-new push channel
// nobody asked for yet, unlike the app's existing pushes which are all triggered by an action the
// user just took (placing a bet, joining a league) - opt-in, not opt-out.
public class UserAlertPreferences
{
    public Guid Id { get; set; }
    public required Guid UserId { get; set; }
    public User? User { get; set; }

    public bool LineupsOut { get; set; }
    public bool Kickoff { get; set; }
    public bool HalfTime { get; set; }
    public bool Goal { get; set; }
    public bool RedCard { get; set; }
    public bool FullTime { get; set; }
}
