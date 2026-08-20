using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiMatchLeaguePreferenceStore : IMatchLeaguePreferenceStore
{
    private const string Key = "fulltime_optional_leagues";

    public Task<string?> GetAsync() => Task.FromResult<string?>(Preferences.Default.Get(Key, string.Empty));

    public Task SetAsync(string value)
    {
        Preferences.Default.Set(Key, value);
        return Task.CompletedTask;
    }
}
