namespace FullTime.App.Shared.Services;

// Device-specific, same pattern as IJwtStore/IPushRegistrar/etc. Used once, on first launch after
// login, to silently set a user's Country (and therefore currency symbol) from their device/browser
// locale - there's deliberately no picker, per the decision that this should just work without
// asking the user anything.
public interface ILocaleProvider
{
    // ISO 3166-1 alpha-2 (e.g. "GB"), or null if the region can't be determined right now.
    Task<string?> GetCountryCodeAsync();
}
