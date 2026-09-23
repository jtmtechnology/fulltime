namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IJwtStore/IActiveContextStore: MAUI backs this with
// Plugin.FirebasePushNotifications; the Web host has no equivalent, so it's a no-op there.
public interface IPushRegistrar
{
    Task RegisterAsync();

    // Must run before AuthState.LogoutAsync - the unregister call needs the still-valid JWT.
    Task UnregisterAsync();
}
