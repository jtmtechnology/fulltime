using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebSlipStore(IJSRuntime js) : ISlipStore
{
    private const string Key = "fulltime_slip";

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

    public async Task SetAsync(string json) => await js.InvokeVoidAsync("localStorage.setItem", Key, json);

    public async Task ClearAsync() => await js.InvokeVoidAsync("localStorage.removeItem", Key);
}
