using System.Globalization;
using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiLocaleProvider : ILocaleProvider
{
    // The device's own OS-level region setting, not the display language - a phone set to English
    // but region "France" should still get €, matching how the OS itself formats currency.
    public Task<string?> GetCountryCodeAsync() =>
        Task.FromResult<string?>(RegionInfo.CurrentRegion.TwoLetterISORegionName);
}
