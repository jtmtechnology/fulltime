namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IPushRegistrar: MAUI backs this with a real IAP purchase
// (Plugin.InAppBilling); the Web host has no store to buy from, so it reports ads as always removed
// there (there's no ad SDK wired up on Web either - see IInterstitialAdService).
public interface IAdsRemovalService
{
    bool AdsRemoved { get; }
    event Action? Changed;

    Task InitializeAsync();
    Task<bool> PurchaseRemoveAdsAsync();
    Task<bool> RestorePurchasesAsync();
}
