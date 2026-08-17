namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IJwtStore — persists which betting pool (Worldwide or
// a specific league) is currently selected. Not sensitive, so MAUI backs it with Preferences
// rather than SecureStorage; the Web host backs it with localStorage via JS interop.
public interface IActiveContextStore
{
    Task<string?> GetAsync();
    Task SetAsync(string value);
    Task ClearAsync();
}
