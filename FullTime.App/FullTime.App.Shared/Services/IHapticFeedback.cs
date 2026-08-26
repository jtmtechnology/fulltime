namespace FullTime.App.Shared.Services;

// Per-host implementation, same pattern as IPushRegistrar. MAUI backs this with a real haptic
// tap; the Web host has no haptics API, so it's a no-op there.
public interface IHapticFeedback
{
    void Tap();
}
