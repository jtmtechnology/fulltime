namespace FullTime.App.Shared.Services;

// The bet slip is disposable UI state, not a credential, so unlike IJwtStore this doesn't need
// secure storage — MAUI backs this with Preferences, Web with localStorage.
public interface ISlipStore
{
    Task<string?> GetAsync();
    Task SetAsync(string json);
    Task ClearAsync();
}
