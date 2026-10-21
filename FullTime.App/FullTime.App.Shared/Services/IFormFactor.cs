namespace FullTime.App.Shared.Services;

public interface IFormFactor
{
    public string GetFormFactor();
    public string GetPlatform();
    public string GetDeviceModel();
    public string GetAppVersion();
}
