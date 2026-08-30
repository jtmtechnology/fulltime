namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IMatchLeaguePreferenceStore: MAUI backs this with
// Preferences; the Web host backs it with browser localStorage via JS interop. Value is a single
// "yyyy-MM-dd|streak" string - the local date of the last spin and the streak count as of that spin.
public interface IDailySpinStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
}
