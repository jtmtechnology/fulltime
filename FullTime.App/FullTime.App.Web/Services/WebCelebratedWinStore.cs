using FullTime.App.Shared.Services;
using Microsoft.JSInterop;

namespace FullTime.App.Web.Services;

public class WebCelebratedWinStore(IJSRuntime js) : ICelebratedWinStore
{
    private const string Key = "fulltime_last_celebrated_win";

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
