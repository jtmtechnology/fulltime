using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// No-op: there's no OS app-icon badge concept for a browser tab.
public class WebBadgeService : IBadgeService
{
    public Task ClearAsync() => Task.CompletedTask;
}
