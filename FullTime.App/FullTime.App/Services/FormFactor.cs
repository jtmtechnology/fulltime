using FullTime.App.Shared.Services;

namespace FullTime.App.Services;

public class FormFactor : IFormFactor
{
    public string GetFormFactor()
    {
        return DeviceInfo.Idiom.ToString();
    }

    public string GetPlatform()
    {
        return DeviceInfo.Platform.ToString() + " - " + DeviceInfo.VersionString;
    }

    public string GetDeviceModel()
    {
        return $"{DeviceInfo.Manufacturer} {DeviceInfo.Model}";
    }

    public string GetAppVersion()
    {
        return $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
    }
}
