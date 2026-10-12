using FullTime.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace FullTime.Api.Controllers;

public record ReportInstallRequest(string Platform, string? AppVersion);

// The app posts here once, on its first launch with nobody logged in. Nothing is stored - the
// "first" is tracked by a flag on the phone - so this only exists to email the owner.
[ApiController]
[Route("api/installs")]
public class InstallsController(OwnerAlerts ownerAlerts) : ControllerBase
{
    [HttpPost]
    public IActionResult Report([FromBody] ReportInstallRequest request)
    {
        if (request.Platform is not ("Android" or "iOS"))
        {
            return BadRequest(new { error = "Platform must be Android or iOS." });
        }

        ownerAlerts.NewInstall(request.Platform, request.AppVersion?[..Math.Min(request.AppVersion.Length, 20)]);
        return NoContent();
    }
}
