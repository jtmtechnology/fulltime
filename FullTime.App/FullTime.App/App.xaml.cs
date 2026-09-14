using FullTime.App.Shared.Services;

namespace FullTime.App;

public partial class App : Application
{
    private readonly IBadgeService _badgeService;

    public App(IBadgeService badgeService)
    {
        InitializeComponent();
        _badgeService = badgeService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "FullTime.App" };

        // Activated fires on cold start too, not just returning from background - both are "the
        // user is looking at the app again", which is exactly when a stale badge should clear. The
        // server always sends aps.badge=1 (see PushNotificationService.cs), so this is the only
        // place that ever resets it back to 0.
        window.Activated += (_, _) => _ = _badgeService.ClearAsync();

        return window;
    }
}
