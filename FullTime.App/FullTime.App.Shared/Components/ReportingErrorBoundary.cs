using FullTime.App.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FullTime.App.Shared.Components;

// An ErrorBoundary that also sends what it caught to the API. The plain one only logs to the
// device, so when App Review hit the "Something went wrong" card on an iPad (2026-10-21) there was
// nothing to go on but their screenshot.
public class ReportingErrorBoundary : ErrorBoundary
{
    [Inject] private ApiClient Api { get; set; } = default!;
    [Inject] private IFormFactor FormFactor { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    protected override async Task OnErrorAsync(Exception exception)
    {
        await base.OnErrorAsync(exception);
        _ = ReportAsync(exception);
    }

    private async Task ReportAsync(Exception exception)
    {
        try
        {
            await Api.ReportClientErrorAsync(Nav.ToBaseRelativePath(Nav.Uri), exception.ToString(),
                FormFactor.GetPlatform(), FormFactor.GetFormFactor(), FormFactor.GetDeviceModel(), FormFactor.GetAppVersion());
        }
        catch
        {
            // Often the network is what failed in the first place; the card is already showing.
        }
    }
}
