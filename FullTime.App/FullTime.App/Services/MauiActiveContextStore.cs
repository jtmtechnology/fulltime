using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiActiveContextStore : IActiveContextStore
{
    private const string Key = "fulltime_active_league";

    public Task<string?> GetAsync() => Task.FromResult<string?>(Preferences.Default.Get(Key, string.Empty));

    public Task SetAsync(string value)
    {
        Preferences.Default.Set(Key, value);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Preferences.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
