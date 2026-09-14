using FullTime.App.Shared.Services;

#if IOS
using UIKit;
#endif

namespace FullTime.App.Services;

// The server always sends aps.badge=1 (see PushNotificationService.cs - nothing here tracks a real
// unread count), so the only client-side job is clearing it back to 0 once the user has actually
// seen the app again. Android's own notification-dot/badge is tied to whichever notifications are
// still active and clears itself as those are dismissed/opened - nothing to do here for it.
public class MauiBadgeService : IBadgeService
{
    public Task ClearAsync()
    {
#if IOS
        UIApplication.SharedApplication.ApplicationIconBadgeNumber = 0;
#endif
        return Task.CompletedTask;
    }
}
