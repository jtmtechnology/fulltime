using Foundation;
using ObjCRuntime;
using Plugin.FirebasePushNotifications;
using UIKit;

namespace FullTime.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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
