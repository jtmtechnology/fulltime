using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebJwtStore(IJSRuntime js) : IJwtStore
{
    private const string Key = "fulltime_token";

    // JS interop can't run during static prerendering (no circuit/DOM yet) — treat that as
    // "not available yet" rather than an error; Routes.razor re-initializes AuthState once the
    // component is actually interactive, which is when this can succeed.
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

    public async Task SetAsync(string token) => await js.InvokeVoidAsync("localStorage.setItem", Key, token);

    public async Task ClearAsync() => await js.InvokeVoidAsync("localStorage.removeItem", Key);
}
