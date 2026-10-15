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
        // Insets usually arrive while the WebView still holds its placeholder document, and the real
        // page load then throws the inline style away - the owner's Samsung ended up with a 0px nav
        // inset and its 3-button nav drawn over the tab labels. So keep re-applying until the script
        // runs in the loaded Blazor page. ~10s of retries covers a slow cold start.
        private const int RetryDelayMs = 250;
        private const int MaxRetries = 40;

        private string script = "";
        private int retriesLeft;

        public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(Android.Views.View v, AndroidX.Core.View.WindowInsetsCompat insets)
        {
            // The root insets as well as the dispatched ones, in case a parent consumes the
            // dispatched insets. Cutout included for teardrop-notch phones.
            var types = AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars()
                | AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout();
            var dispatched = insets.GetInsets(types);
            var root = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(v)?.GetInsets(types);
            var density = v.Resources?.DisplayMetrics?.Density ?? 1f;
            var topPx = Math.Max(dispatched.Top, root?.Top ?? 0) / density;
            var bottomPx = Math.Max(dispatched.Bottom, root?.Bottom ?? 0) / density;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            script =
                "var s=document.documentElement.style;" +
                $"s.setProperty('--android-status-inset-top','{topPx.ToString(culture)}px');" +
                $"s.setProperty('--android-nav-inset-bottom','{bottomPx.ToString(culture)}px');" +
                "document.readyState==='complete'&&!!window.Blazor";
            retriesLeft = MaxRetries;
            Apply();
            return insets;
        }

        private void Apply() => webView.EvaluateJavascript(script, new AppliedCallback(this));

        private void RetryUnlessApplied(Java.Lang.Object? result)
        {
            if (result?.ToString() == "true" || retriesLeft-- <= 0)
                return;
            webView.PostDelayed(Apply, RetryDelayMs);
        }

        private sealed class AppliedCallback(SafeAreaInsetListener owner) : Java.Lang.Object, Android.Webkit.IValueCallback
        {
            public void OnReceiveValue(Java.Lang.Object? value) => owner.RetryUnlessApplied(value);
        }
    }
#endif
}
