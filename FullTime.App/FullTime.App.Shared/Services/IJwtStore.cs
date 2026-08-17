namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IFormFactor: MAUI backs this with SecureStorage
// (it's a credential); the Web host backs it with browser localStorage via JS interop.
public interface IJwtStore
{
    Task<string?> GetAsync();
    Task SetAsync(string token);
    Task ClearAsync();
}
