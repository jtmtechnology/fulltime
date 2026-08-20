namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IJwtStore/IActiveContextStore: MAUI backs this with
// Preferences; the Web host backs it with browser localStorage via JS interop.
public interface IMatchLeaguePreferenceStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
}
