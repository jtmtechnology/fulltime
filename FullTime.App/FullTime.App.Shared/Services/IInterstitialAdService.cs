namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IPushRegistrar. MAUI backs this with a real AdMob
// interstitial; the Web host has no ad SDK wired up, so it's a no-op there. Both trigger points
// (startup, after placing a bet) are deliberately just two fixed hooks, not a general-purpose ad
// scheduler - no frequency capping beyond "once per app launch" / "once per bet placed", per the
// explicit ask. Revisit if that turns out to be too aggressive once it's actually in front of people.
public interface IInterstitialAdService
{
    // Fire-and-forget from the caller's perspective - preloads and shows on cold launch only
    // (MainLayout.OnInitializedAsync runs once per app session). A no-op once ads are removed.
    Task ShowOnStartupAsync();

    // Called right after a bet places successfully. A no-op once ads are removed.
    Task ShowAfterBetPlacedAsync();
}
