using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

// No-op: no ad SDK wired up on the Web host - ads are a mobile-app monetization play (see
// IAdsRemovalService), not something shown in the browser client.
public class WebInterstitialAdService : IInterstitialAdService
{
    public Task ShowOnStartupAsync() => Task.CompletedTask;
    public Task ShowAfterBetPlacedAsync() => Task.CompletedTask;
}
