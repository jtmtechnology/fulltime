namespace FullTime.App.Shared.Services;

// Per-host implementation: MAUI raises Resumed from Window.Resumed (app brought back to the
// foreground, not the initial launch); the Web host never raises it.
public interface IAppLifecycle
{
    event Action? Resumed;
}
