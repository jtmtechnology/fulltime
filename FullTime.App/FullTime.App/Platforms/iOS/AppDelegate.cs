using AppTrackingTransparency;
using Foundation;
using ObjCRuntime;
using Plugin.FirebasePushNotifications;
using UIKit;

namespace FullTime.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    private static bool _trackingAuthorizationRequested;

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    // Confirmed missing on a real ad-hoc build: no ads ever showed. AdMob's iOS setup requires the
    // app to actually make this request (declining is fine - the SDK falls back to non-personalized
    // ads - but never asking at all is a documented cause of ads failing to fill entirely).
    // OnActivated fires every time the app returns to foreground, so guard to once per app run.
    public override void OnActivated(UIApplication application)
    {
        base.OnActivated(application);

        if (_trackingAuthorizationRequested)
        {
            return;
        }
        _trackingAuthorizationRequested = true;

        if (ATTrackingManager.TrackingAuthorizationStatus == ATTrackingManagerAuthorizationStatus.NotDetermined)
        {
            _ = ATTrackingManager.RequestTrackingAuthorizationAsync();
        }
    }

    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    [BindingImpl(BindingImplOptions.GeneratedCode | BindingImplOptions.Optimizable)]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
    {
        IFirebasePushNotification.Current.RegisteredForRemoteNotifications(deviceToken);
    }

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    [BindingImpl(BindingImplOptions.GeneratedCode | BindingImplOptions.Optimizable)]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
    {
        IFirebasePushNotification.Current.FailedToRegisterForRemoteNotifications(error);
    }

    [Export("application:didReceiveRemoteNotification:fetchCompletionHandler:")]
    public void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
    {
        IFirebasePushNotification.Current.DidReceiveRemoteNotification(userInfo);
        completionHandler(UIBackgroundFetchResult.NewData);
    }
}
