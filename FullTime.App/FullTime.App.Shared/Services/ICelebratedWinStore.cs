namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IMatchLeaguePreferenceStore: MAUI backs this with
// Preferences; the Web host backs it with browser localStorage via JS interop. Holds the Id of
// the most recently settled Won bet the user has already been shown a celebration for, so the
// win-celebration overlay fires once per new win instead of on every app open.
public interface ICelebratedWinStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
}
