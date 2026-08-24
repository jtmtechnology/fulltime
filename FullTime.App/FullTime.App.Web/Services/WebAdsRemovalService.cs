using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// No-op: the Web host has no store to buy from, so ads (which aren't shown there either - see
// WebInterstitialAdService) are always reported as removed.
public class WebAdsRemovalService : IAdsRemovalService
{
    public bool AdsRemoved => true;
    public event Action? Changed { add { } remove { } }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task<bool> PurchaseRemoveAdsAsync() => Task.FromResult(true);
    public Task<bool> RestorePurchasesAsync() => Task.FromResult(true);
}
