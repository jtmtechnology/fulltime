using Microsoft.Extensions.Logging;
using FullTime.App.Shared.Services;
using FullTime.App.Services;
using Plugin.FirebasePushNotifications;

namespace FullTime.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseFirebasePushNotifications()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Add device-specific services used by the FullTime.App.Shared project
        builder.Services.AddSingleton<IFormFactor, FormFactor>();
        builder.Services.AddScoped<IJwtStore, MauiJwtStore>();
        builder.Services.AddScoped<ISlipStore, MauiSlipStore>();
        builder.Services.AddScoped<IActiveContextStore, MauiActiveContextStore>();
        builder.Services.AddScoped<IPushRegistrar, MauiPushRegistrar>();
        builder.Services.AddScoped<IMatchLeaguePreferenceStore, MauiMatchLeaguePreferenceStore>();
        builder.Services.AddScoped<AuthState>();
        builder.Services.AddScoped<BetSlipState>();
        builder.Services.AddScoped<ActiveContextState>();
        builder.Services.AddScoped<MatchLeaguePreferences>();
        builder.Services.AddHttpClient<ApiClient>(client => client.BaseAddress = new Uri(ApiConfig.BaseUrl));

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        // AddBlazorWebViewDeveloperTools() draws a native floating badge over the WebView (top-right,
        // same spot as our own nav toggle) to jump into Chrome DevTools — it was eating real taps on
        // our hamburger button underneath it. Not worth it for this app; leave it off.
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
