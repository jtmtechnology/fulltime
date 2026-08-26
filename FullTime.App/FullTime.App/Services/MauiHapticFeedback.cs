namespace FullTime.App.Services;

// MAUI's own Microsoft.Maui.Devices.IHapticFeedback (globally-usinged by the SDK) collides by name
// with our shared abstraction, so the implemented interface is qualified explicitly.
public class MauiHapticFeedback : FullTime.App.Shared.Services.IHapticFeedback
{
    public void Tap()
    {
        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Some emulators/devices don't expose a vibration motor, or lack the VIBRATE
            // permission - caught broadly (not just FeatureNotSupportedException) because a
            // missed haptic silently breaking the caller's own flow (see BetSlipSheet's
            // PlaceBetAsync, which got stuck forever on an uncaught exception here) is a much
            // worse outcome than a missed buzz.
        }
    }
}
