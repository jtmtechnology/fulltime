using FullTime.App.Shared.Services;
using Plugin.MauiMtAdmob;
using Plugin.MauiMtAdmob.Extra;
#if ANDROID
using Microsoft.Maui.ApplicationModel;
#endif

namespace FullTime.App.Services;

// Real implementation of IInterstitialAdService for MAUI, backed by Plugin.MauiMtAdmob
// (CrossMauiMTAdmob.Current). Ad unit IDs below are FullTime's real AdMob IDs - confirmed working
// with Google's TEST IDs first (see git history), then swapped once the real AdMob account/app/ad
// units were created.
//
// Confirmed on both a real iPhone (ad-hoc build) and the Android emulator: no ad ever showed. Root
// cause - .UseMauiMTAdmob() in MauiProgram only registers the plugin's DI wiring, it does NOT
// initialize it. IMTAdmob.IsPluginInitialised stays false, and Load/Show silently no-op, until
// Init(...) is called explicitly - and Android's and iOS's Init overloads take genuinely different
// parameters (confirmed via reflection against the installed 2.4.0 package: Android's takes a
// MauiAppCompatActivity + explicit appId; iOS's takes neither, since the App ID there already comes
// from Info.plist's GADApplicationIdentifier). handleTrackingAuthorization: true on iOS means the
// plugin's Init call handles the App Tracking Transparency prompt itself, so AppDelegate no longer
// needs to request it separately.
//
// IMTAdmob's readiness check is poll-based (IsInterstitialLoaded()), not event-based, despite the
// interface exposing OnInterstitialLoaded/OnInterstitialFailedToLoad events - polling is simpler and
// avoids a second place needing to track load state. The plugin can only hold one loaded
// interstitial at a time (multi-load is a licensed-version feature), so this preloads the next ad
// right after showing one, rather than loading fresh on every call.
public class MauiInterstitialAdService(IAdsRemovalService adsRemoval) : IInterstitialAdService
{
    private static string AdUnitId => DeviceInfo.Platform == DevicePlatform.iOS
        ? "ca-app-pub-8873351312647846/8091028374"  // FullTime interstitial ad unit (iOS)
        : "ca-app-pub-8873351312647846/9075922506"; // FullTime interstitial ad unit (Android)

    private static bool _initialized;

    public Task ShowOnStartupAsync() => ShowAsync();

    public Task ShowAfterBetPlacedAsync() => ShowAsync();

    private async Task ShowAsync()
    {
        if (adsRemoval.AdsRemoved)
        {
            return;
        }

        EnsureInitialized();

        if (!CrossMauiMTAdmob.Current.IsInterstitialLoaded())
        {
            await LoadAndWaitAsync();
        }

        if (CrossMauiMTAdmob.Current.IsInterstitialLoaded())
        {
            CrossMauiMTAdmob.Current.ShowInterstitial();
        }
        // Otherwise: no ad ready in time (slow/no connection) - skip silently rather than block
        // the user's flow waiting for one.

        // Preload the next one now regardless, so the *next* trigger (startup vs post-bet, whichever
        // comes next) has a ready ad instead of paying the load latency again.
        if (!CrossMauiMTAdmob.Current.IsInterstitialLoaded())
        {
            CrossMauiMTAdmob.Current.LoadInterstitial(AdUnitId);
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

#if ANDROID
        const string appId = "ca-app-pub-8873351312647846~5927014987"; // FullTime AdMob app ID (Android)
        var activity = Platform.CurrentActivity as Microsoft.Maui.MauiAppCompatActivity;
        CrossMauiMTAdmob.Current.Init(
            activity!, appId, license: null!, nativeAdsId: null!, openAdsId: null!,
            enableOpenAds: false, tagForUnderAgeOfConsent: false, testDeviceId: null!,
            forceTesting: false, geography: DebugGeography.DEBUG_GEOGRAPHY_DISABLED,
            initialiseConsentAtStartup: false, debugMode: false);
#elif IOS
        CrossMauiMTAdmob.Current.Init(
            license: null!, nativeAdsId: null!, openAdsId: null!, enableOpenAds: false,
            tagForUnderAgeOfConsent: false, testDeviceIds: [], geography: DebugGeography.DEBUG_GEOGRAPHY_DISABLED,
            initialiseConsentAtStartup: false, debugMode: false, handleTrackingAuthorization: true);
#endif
    }

    private static async Task LoadAndWaitAsync()
    {
        CrossMauiMTAdmob.Current.LoadInterstitial(AdUnitId);

        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!CrossMauiMTAdmob.Current.IsInterstitialLoaded() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
    }
}
