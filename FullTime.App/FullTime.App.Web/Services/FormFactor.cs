using FullTime.App.Shared.Services;

namespace FullTime.App.Web.Services;

public class FormFactor : IFormFactor
{
    public string GetFormFactor()
    {
        return "Web";
    }

    public string GetPlatform()
    {
        return Environment.OSVersion.ToString();
    }

    public string GetDeviceModel()
    {
        return "Web";
    }

    public string GetAppVersion()
    {
        return typeof(FormFactor).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
