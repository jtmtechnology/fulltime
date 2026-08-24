using FullTime.App.Shared.Services;
using Plugin.InAppBilling;

namespace FullTime.App.Services;

// Real implementation of IAdsRemovalService for MAUI, backed by Plugin.InAppBilling. "remove_ads"
// is a non-consumable, one-off product - must be created with that exact ID in both App Store
// Connect and the Google Play Console before this can succeed for real; until then PurchaseAsync
// will fail (there's nothing to buy). Caches the owned/not-owned result in Preferences so
// IInterstitialAdService can check it synchronously on every ad trigger without hitting the store
// each time - RestorePurchasesAsync re-syncs that cache from the store's own record (needed on a
// fresh install/device per store guidelines for non-consumables).
public class MauiAdsRemovalService : IAdsRemovalService
{
    public const string RemoveAdsProductId = "remove_ads";
    private const string CacheKey = "ads_removed";

    public bool AdsRemoved { get; private set; }
    public event Action? Changed;

    public Task InitializeAsync()
    {
        AdsRemoved = Preferences.Get(CacheKey, false);
        return Task.CompletedTask;
    }

    public async Task<bool> PurchaseRemoveAdsAsync()
    {
        var billing = CrossInAppBilling.Current;

        try
        {
            var connected = await billing.ConnectAsync();
            if (!connected)
            {
                return false;
            }

            var purchase = await billing.PurchaseAsync(RemoveAdsProductId, ItemType.InAppPurchase);
            if (purchase is null)
            {
                // Null covers both "user cancelled" and "already owned" (some platforms return the
                // existing purchase instead of null here) - RestorePurchasesAsync is the reliable
                // path for "already owned", so a null purchase here is just treated as not-bought.
                return false;
            }

            SetOwned(true);
            return true;
        }
        catch
        {
            // A failed/cancelled purchase isn't worth surfacing as a crash - the caller's UI just
            // stays on "buy ads removal" and the user can try again.
            return false;
        }
        finally
        {
            await billing.DisconnectAsync();
        }
    }

    public async Task<bool> RestorePurchasesAsync()
    {
        var billing = CrossInAppBilling.Current;

        try
        {
            var connected = await billing.ConnectAsync();
            if (!connected)
            {
                return AdsRemoved;
            }

            var purchases = await billing.GetPurchasesAsync(ItemType.InAppPurchase);
            var owned = purchases?.Any(p => p.ProductId == RemoveAdsProductId) ?? false;
            SetOwned(owned);
            return owned;
        }
        catch
        {
            return AdsRemoved;
        }
        finally
        {
            await billing.DisconnectAsync();
        }
    }

    private void SetOwned(bool owned)
    {
        if (owned == AdsRemoved)
        {
            return;
        }

        AdsRemoved = owned;
        Preferences.Set(CacheKey, owned);
        Changed?.Invoke();
    }
}
