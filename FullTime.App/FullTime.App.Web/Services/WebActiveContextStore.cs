using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebActiveContextStore(IJSRuntime js) : IActiveContextStore
{
    private const string Key = "fulltime_active_league";

    // JS interop can't run during static prerendering — treat that as "not set yet" rather than
    // an error, same pattern as WebJwtStore.
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

    public async Task ClearAsync() => await js.InvokeVoidAsync("localStorage.removeItem", Key);
}
