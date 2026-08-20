using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebMatchLeaguePreferenceStore(IJSRuntime js) : IMatchLeaguePreferenceStore
{
    private const string Key = "fulltime_optional_leagues";

    public async Task<string?> GetAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Key);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SetAsync(string value) => await js.InvokeVoidAsync("localStorage.setItem", Key, value);
}
