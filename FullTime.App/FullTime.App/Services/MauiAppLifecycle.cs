using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class MauiAppLifecycle : IAppLifecycle
{
    public event Action? Resumed;

    public void RaiseResumed() => Resumed?.Invoke();
}
