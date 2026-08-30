using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiDailySpinStore : IDailySpinStore
{
    private const string Key = "fulltime_daily_spin";

    public Task<string?> GetAsync() => Task.FromResult<string?>(Preferences.Default.Get(Key, string.Empty));

    public Task SetAsync(string value)
    {
        Preferences.Default.Set(Key, value);
        return Task.CompletedTask;
    }
}
