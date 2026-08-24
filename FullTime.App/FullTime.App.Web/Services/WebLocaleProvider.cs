using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebLocaleProvider(IJSRuntime js) : ILocaleProvider
{
    // Same prerender caveat as WebJwtStore — JS interop can't run until the interactive circuit
    // exists, so this returns null during static prerender and MainLayout simply tries again next
    // time (it only ever calls this while the user's Country is still unset).
    public async Task<string?> GetCountryCodeAsync()
    {
        try
        {
            // navigator.language is a BCP 47 tag like "en-GB" — the part after the last hyphen is
            // the region when present; a bare tag like "en" (no region) has none to extract.
            var language = await js.InvokeAsync<string?>("fullTimeGetLocale");
            var parts = language?.Split('-');
            return parts is { Length: > 1 } ? parts[^1].ToUpperInvariant() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
