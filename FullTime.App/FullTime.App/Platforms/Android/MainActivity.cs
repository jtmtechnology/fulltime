using Android.App;
using Android.Content.PM;
using Android.OS;

namespace FullTime.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // MAUI already lays the WebView out edge-to-edge, but the system still paints its own bar
        // backgrounds over it: below Android 15, the theme's leftover purple behind the status bar;
        // on every version, a grey scrim behind 3-button nav, which hid the tab bar labels on a
        // phone. The app's own top bar and tab bar are dark, so the bar icons need to be light too.
        // SystemBarStyle.Dark = transparent bars with light icons regardless of the phone's own
        // light/dark mode (the default "auto" style follows it, which gave dark icons on a light-mode
        // phone). It also sets the bar colours below API 35, where they're still honoured.
        var transparent = Android.Graphics.Color.Transparent;
        AndroidX.Activity.EdgeToEdge.Enable(this,
            AndroidX.Activity.SystemBarStyle.Dark(transparent),
            AndroidX.Activity.SystemBarStyle.Dark(transparent));

        if (Window is { } window && Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            window.NavigationBarContrastEnforced = false;
            window.StatusBarContrastEnforced = false;
        }
    }
}
