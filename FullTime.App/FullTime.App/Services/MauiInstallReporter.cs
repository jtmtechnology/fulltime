using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

// Tells the API (and so the owner, by email) the first time the app is opened on a phone. Skipped
// when a login is already stored, so existing users updating to the first build with this don't
// all show up as "new".
public class MauiInstallReporter(ApiClient api, IJwtStore jwtStore)
{
    private const string ReportedKey = "fulltime_install_reported";

    public async Task ReportIfFirstLaunchAsync()
    {
        if (Preferences.Default.Get(ReportedKey, false))
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(await jwtStore.GetAsync()))
            {
                var platform = DeviceInfo.Platform == DevicePlatform.iOS ? "iOS" : "Android";
                await api.ReportInstallAsync(platform, AppInfo.Current.VersionString);
            }

            Preferences.Default.Set(ReportedKey, true);
        }
        catch (Exception)
        {
            // Offline or API down - leave the flag unset and try again next launch.
        }
    }
}
