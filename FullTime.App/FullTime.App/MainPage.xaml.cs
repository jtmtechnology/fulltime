using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace FullTime.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

#if IOS
        // ContentPage insets natively below the status bar/notch by default on iOS, but app.css
        // also adds its own env(safe-area-inset-top) padding for Android (which doesn't inset
        // natively) — stacking both produced a double gap above the header. Going edge-to-edge here
        // makes CSS's safe-area padding the single source of truth on every platform.
        On<iOS>().SetUseSafeArea(false);
#endif

        // BlazorWebView.BackgroundColor (set in XAML) covers the .NET control, but on Android the
        // native WebView it wraps still paints white for the gap before the page's own CSS loads —
        // that's the flash the user sees right after the splash screen. Setting the platform
        // WebView's background directly closes that gap.
        blazorWebView.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView platformWebView)
            {
                platformWebView.SetBackgroundColor(Android.Graphics.Color.ParseColor("#0D1117"));
            }
#endif
        };
    }
}
