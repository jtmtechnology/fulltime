using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace FullTime.Api.Controllers;

public record ClientErrorReport(
    string? Page, string? Exception, string? Platform, string? FormFactor, string? DeviceModel, string? AppVersion);

// The app's "Something went wrong" card swallows the exception it caught, so without this the only
// evidence of an on-device failure was a screenshot (Apple's iPad rejection, 2026-10-21, with nothing
// in the server logs). Anonymous because the failing screen may be login/register; a token, if
// sent, just names the user.
[ApiController]
[Route("api/client-errors")]
public class ClientErrorsController(OwnerAlerts ownerAlerts, ILogger<ClientErrorsController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Report([FromBody] ClientErrorReport report)
    {
        var page = Trim(report.Page, 300);
        var exception = Trim(report.Exception, 8000);
        var device = $"{Trim(report.Platform, 60)} / {Trim(report.FormFactor, 30)} / {Trim(report.DeviceModel, 60)}";
        var appVersion = Trim(report.AppVersion, 40);
        var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "anonymous";

        logger.LogWarning("Client error on {Page} (user {UserId}, {Device}, app {AppVersion}):\n{Exception}",
            page, userId, device, appVersion, exception);
        ownerAlerts.ClientError(page, userId, device, appVersion, exception);
        return NoContent();
    }

    private static string Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Length <= max ? value : value[..max];
}
