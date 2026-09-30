using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// The web head isn't in active use, so no page-visibility hook - wins are still celebrated on load.
public class WebAppLifecycle : IAppLifecycle
{
#pragma warning disable CS0067
    public event Action? Resumed;
#pragma warning restore CS0067
}
