using FullTime.App.Shared.Services;
using Plugin.MauiMtAdmob;

namespace FullTime.App.Services;

// Real implementation of IInterstitialAdService for MAUI, backed by Plugin.MauiMtAdmob
// (CrossMauiMTAdmob.Current). Ad unit IDs below are Google's own published TEST IDs - safe to ship
// while developing, but MUST be swapped for real ad unit IDs from the AdMob console before this
// goes to the App Store / Play Store, or no real ad (and no real revenue) will ever show.
//
// IMTAdmob's public surface is poll-based, not event-based (confirmed against the installed 2.4.0
// package - it exposes LoadInterstitial/IsInterstitialLoaded/ShowInterstitial only), so readiness
// is checked by polling IsInterstitialLoaded() rather than awaiting a load-completed event. The
// plugin can only hold one loaded interstitial at a time (multi-load is a licensed-version
// feature), so this preloads the next ad right after showing one, rather than loading fresh on
// every call - keeps the post-bet ad from making the user wait on a cold load most of the time.
public class MauiInterstitialAdService(IAdsRemovalService adsRemoval) : IInterstitialAdService
{
    private static string AdUnitId => DeviceInfo.Platform == DevicePlatform.iOS
        ? "ca-app-pub-3940256099942544/4411468910"  // Google TEST interstitial ad unit (iOS)
        : "ca-app-pub-3940256099942544/1033173712"; // Google TEST interstitial ad unit (Android)

    public Task ShowOnStartupAsync() => ShowAsync();

    public Task ShowAfterBetPlacedAsync() => ShowAsync();

    private async Task ShowAsync()
    {
        if (adsRemoval.AdsRemoved)
        {
            return;
        }

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
