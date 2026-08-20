using FullTime.App.Shared.Services;
using Plugin.FirebasePushNotifications;

namespace FullTime.App.Services;

// Real implementation of IPushRegistrar for MAUI, backed by Plugin.FirebasePushNotifications.
public class MauiPushRegistrar(
    IFirebasePushNotification pushNotification,
    INotificationPermissions notificationPermissions,
    ApiClient api) : IPushRegistrar
{
    private static string PlatformName => DeviceInfo.Platform == DevicePlatform.iOS ? "iOS" : "Android";

    private bool _subscribed;
    private Task? _registrationTask;

    // AuthState.Changed can fire more than once per app session (e.g. Routes.razor re-runs
    // AuthState.InitializeAsync on first render as a Web-host prerendering workaround), and
    // MainLayout calls RegisterAsync on every Changed event. Without this guard, that requests
    // native notification permission more than once in quick succession — which is what made the
    // iOS "Allow notifications" prompt appear twice. Registration only needs to happen once per
    // session; TokenRefreshed already covers any later token changes.
    public Task RegisterAsync() => _registrationTask ??= RegisterCoreAsync();

    private async Task RegisterCoreAsync()
    {
        await notificationPermissions.RequestPermissionAsync();

        if (!_subscribed)
        {
            pushNotification.TokenRefreshed += (_, e) => _ = SendTokenAsync(e.Token);
            _subscribed = true;
        }

        await pushNotification.RegisterForPushNotificationsAsync();

        if (pushNotification.Token is { } token)
        {
            await SendTokenAsync(token);
        }
    }

    private async Task SendTokenAsync(string token)
    {
        try
        {
            await api.RegisterDeviceAsync(token, PlatformName);
        }
        catch
        {
            // Best-effort — a friend missing one push notification isn't worth surfacing an error for.
        }
    }
}
