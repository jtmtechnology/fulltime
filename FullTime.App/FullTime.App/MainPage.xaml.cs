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
            }
#endif
        };
    }
}
