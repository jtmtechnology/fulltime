using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiSlipStore : ISlipStore
{
    private const string Key = "fulltime_slip";

    public Task<string?> GetAsync() => Task.FromResult(Preferences.Default.Get<string?>(Key, null));

    public Task SetAsync(string json)
    {
        Preferences.Default.Set(Key, json);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Preferences.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
