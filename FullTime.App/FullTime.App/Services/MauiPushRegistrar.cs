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

    public async Task RegisterAsync()
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
