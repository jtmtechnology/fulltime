using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiJwtStore : IJwtStore
{
    private const string Key = "fulltime_token";

    public Task<string?> GetAsync() => SecureStorage.Default.GetAsync(Key);

    public Task SetAsync(string token) => SecureStorage.Default.SetAsync(Key, token);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}
