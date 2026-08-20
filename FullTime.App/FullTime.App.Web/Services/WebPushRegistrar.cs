using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// No-op: the Web host has no push notification support.
public class WebPushRegistrar : IPushRegistrar
{
    public Task RegisterAsync() => Task.CompletedTask;
}
