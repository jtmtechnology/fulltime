using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// No-op: no haptics API in the browser.
public class WebHapticFeedback : IHapticFeedback
{
    public void Tap()
    {
    }
}
