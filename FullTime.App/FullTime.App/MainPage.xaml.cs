namespace FullTime.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

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

                // Android's WebView doesn't populate CSS env(safe-area-inset-bottom) the way iOS's
                // WKWebView does, and Android 15 (this app's target) enforces edge-to-edge layout, so
                // the WebView draws straight under the real nav bar (3-button/gesture/Samsung) with no
                // way for app.css to know its height. Bridge the real inset in as a CSS variable.
                AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(platformWebView, new SafeAreaInsetListener(platformWebView));
                platformWebView.Post(() => AndroidX.Core.View.ViewCompat.RequestApplyInsets(platformWebView));
            }
#endif
        };
    }

#if ANDROID
    // Java.Lang.Object base needed for a Java peer - AndroidX callback interfaces like this one
    // can't be implemented on a plain C# class the way a .NET interface can.
    private sealed class SafeAreaInsetListener(Android.Webkit.WebView webView)
        : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
    {
        public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(Android.Views.View v, AndroidX.Core.View.WindowInsetsCompat insets)
        {
            var systemBars = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
            var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
            var bottomPx = systemBars.Bottom / density;
            webView.EvaluateJavascript(
                $"document.documentElement.style.setProperty('--android-nav-inset-bottom','{bottomPx.ToString(System.Globalization.CultureInfo.InvariantCulture)}px')",
                null);
            return insets;
        }
    }
#endif
}
