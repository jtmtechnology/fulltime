namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IMatchLeaguePreferenceStore: MAUI backs this with
// Preferences; the Web host backs it with browser localStorage via JS interop. Holds the
// SettledAt (round-trip "O" format) of the most recent Won bet the user has already been shown a
// celebration for - every won bet settled after that point is still-unseen, so the overlay can
// queue and show each of them in turn instead of only ever the single latest win.
public interface ICelebratedWinStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
}
