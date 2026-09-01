using FullTime.App.Shared.Services;
using Plugin.MauiMtAdmob;
using Plugin.MauiMtAdmob.Extra;
#if ANDROID
using Microsoft.Maui.ApplicationModel;
using Xamarin.Google.UserMesssagingPlatform;
#elif IOS
using MT.UMP.iOS;
#endif

namespace FullTime.App.Services;

// Real implementation of IInterstitialAdService for MAUI, backed by Plugin.MauiMtAdmob
// (CrossMauiMTAdmob.Current). Ad unit IDs below are Google's published TEST IDs - AdMob only
// serves real ads to apps actually live on the Play Store / App Store (an anti-invalid-traffic
// policy, not a bug here), so these stay in place until FullTime is actually published. Swap back
// to FullTime's real IDs (see git history, commit c95785b) at that point - test IDs MUST NOT ship
// to the App Store / Play Store themselves, they earn no real revenue.
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
//
// UMP (User Messaging Platform / GDPR consent) is wired up directly against Google's own SDK
// (RequestConsentAsync below), NOT through Plugin.MauiMtAdmob's own consent-form support - that's a
// paid add-on from the plugin's vendor (undisclosed price; see hightouchinnovation.com/MMTAdmob).
// The plugin's own docs explicitly say to "implement your choice of Certified CMP" on the unlicensed
// path, and Google's UMP is itself a Certified CMP, so this calls it for free: Microsoft's own
// Xamarin.Google.UserMessagingPlatform binding on Android, a free MIT binding from the same author
// as the ad plugin (MTAdmob.UMP.iOS.Binding) on iOS. Consent must be resolved before the ad SDK
// itself initializes (Google's documented ordering - initializing first can serve a personalised ad
// before consent is known), hence RequestConsentAsync runs ahead of EnsureInitialized below.
public class MauiInterstitialAdService(IAdsRemovalService adsRemoval) : IInterstitialAdService
{
    private static string AdUnitId => DeviceInfo.Platform == DevicePlatform.iOS
        ? "ca-app-pub-3940256099942544/4411468910"  // Google TEST interstitial ad unit (iOS)
        : "ca-app-pub-3940256099942544/1033173712"; // Google TEST interstitial ad unit (Android)

    private static bool _initialized;
    private static bool _consentRequested;

    public Task ShowOnStartupAsync() => ShowAsync();

    public Task ShowAfterBetPlacedAsync() => ShowAsync();

    private async Task ShowAsync()
    {
        if (adsRemoval.AdsRemoved)
        {
            return;
        }

        await RequestConsentAsync();
        EnsureInitialized();

        if (!CrossMauiMTAdmob.Current.IsInterstitialLoaded())
        {
            await LoadAndWaitAsync();
        }

        if (CrossMauiMTAdmob.Current.IsInterstitialLoaded())
        {
            // Callers (e.g. the startup win-celebration check) need to know the ad has actually
            // been dismissed, not just that ShowInterstitial() was called - it displays natively
            // and returns immediately, well before the user has closed it.
            await ShowAndWaitForCloseAsync();
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

    private static async Task ShowAndWaitForCloseAsync()
    {
        var tcs = new TaskCompletionSource();
        EventHandler? onClosed = null;
        onClosed = (_, _) =>
        {
            CrossMauiMTAdmob.Current.OnInterstitialClosed -= onClosed;
            tcs.TrySetResult();
        };
        CrossMauiMTAdmob.Current.OnInterstitialClosed += onClosed;

        CrossMauiMTAdmob.Current.ShowInterstitial();

        // Safety net in case the close event never fires for some reason - don't block whatever
        // is waiting on this (e.g. the win-celebration check) forever.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        if (completed != tcs.Task)
        {
            CrossMauiMTAdmob.Current.OnInterstitialClosed -= onClosed;
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
        const string appId = "ca-app-pub-3940256099942544~3347511713"; // Google TEST AdMob app ID (Android)
        var activity = Platform.CurrentActivity as Microsoft.Maui.MauiAppCompatActivity;
        CrossMauiMTAdmob.Current.Init(
            activity!, appId, license: null!, nativeAdsId: null!, openAdsId: null!,
            enableOpenAds: false, tagForUnderAgeOfConsent: false, testDeviceId: null!,
            forceTesting: false, geography: DebugGeography.DEBUG_GEOGRAPHY_DISABLED,
            initialiseConsentAtStartup: false, debugMode: true);
#elif IOS
        CrossMauiMTAdmob.Current.Init(
            license: null!, nativeAdsId: null!, openAdsId: null!, enableOpenAds: false,
            tagForUnderAgeOfConsent: false, testDeviceIds: [], geography: DebugGeography.DEBUG_GEOGRAPHY_DISABLED,
            initialiseConsentAtStartup: false, debugMode: true, handleTrackingAuthorization: true);
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

    // Once per app session, ahead of ad-SDK init - see this file's header comment for why. A failed
    // info-update (e.g. no network on cold launch) isn't retried this session; the ad SDK still
    // initializes either way below, same as if consent simply wasn't required.
    private static async Task RequestConsentAsync()
    {
        if (_consentRequested)
        {
            return;
        }
        _consentRequested = true;

        try
        {
#if ANDROID
            await AndroidRequestConsentAsync();
#elif IOS
            await IosRequestConsentAsync();
#endif
        }
        catch
        {
            // Best-effort - a consent-flow hiccup must not block ads (or the rest of the app) from
            // working. Google's own SDK behaves conservatively (non-personalised ads) when consent
            // state is unknown, so failing open here is safe.
        }
    }

#if ANDROID
    // Implements all three of UMP's single-method listener interfaces on one object rather than
    // three separate classes - RequestConsentInfoUpdate and LoadAndShowConsentFormIfRequired each
    // only need whichever pair/single they actually take.
    private sealed class ConsentCallback : Java.Lang.Object,
        IConsentInformationOnConsentInfoUpdateSuccessListener,
        IConsentInformationOnConsentInfoUpdateFailureListener,
        IConsentFormOnConsentFormDismissedListener
    {
        public readonly TaskCompletionSource<bool> InfoUpdateTcs = new();
        public readonly TaskCompletionSource FormDismissedTcs = new();

        public void OnConsentInfoUpdateSuccess() => InfoUpdateTcs.TrySetResult(true);
        public void OnConsentInfoUpdateFailure(FormError p0) => InfoUpdateTcs.TrySetResult(false);
        public void OnConsentFormDismissed(FormError? p0) => FormDismissedTcs.TrySetResult();
    }

    private static async Task AndroidRequestConsentAsync()
    {
        if (Platform.CurrentActivity is not Microsoft.Maui.MauiAppCompatActivity activity)
        {
            return;
        }

        var consentInfo = UserMessagingPlatform.GetConsentInformation(activity);
        var parameters = new ConsentRequestParameters.Builder().Build();
        var callback = new ConsentCallback();

        consentInfo.RequestConsentInfoUpdate(activity, parameters, callback, callback);
        if (!await callback.InfoUpdateTcs.Task)
        {
            return;
        }

        // No-ops internally (calls straight back) if no form is actually required for this user -
        // same "only shows if required" behaviour as the iOS side's LoadAndPresentIfRequired.
        UserMessagingPlatform.LoadAndShowConsentFormIfRequired(activity, callback);
        await callback.FormDismissedTcs.Task;
    }
#elif IOS
    private static async Task IosRequestConsentAsync()
    {
        var consentInfo = UMPConsentInformation.SharedInstance;
        var parameters = new UMPRequestParameters();

        var updateTcs = new TaskCompletionSource();
        consentInfo.RequestConsentInfoUpdate(parameters, _ => updateTcs.TrySetResult());
        await updateTcs.Task;

        var viewController = Microsoft.Maui.ApplicationModel.Platform.GetCurrentUIViewController();
        if (viewController is null)
        {
            return;
        }

        var dismissedTcs = new TaskCompletionSource();
        UMPConsentForm.LoadAndPresentIfRequired(viewController, _ => dismissedTcs.TrySetResult());
        await dismissedTcs.Task;
    }
#endif
}
