using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record RegisterDeviceRequest(string Token, DevicePlatform Platform);

[ApiController]
[Route("api/devices")]
[Authorize]
public class DevicesController(AppDbContext db) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDeviceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Token is required." });
        }

        var existing = await db.DeviceTokens.SingleOrDefaultAsync(d => d.Token == request.Token, ct);
        if (existing is not null)
        {
            // The same physical device token can end up registered against a different account
            // if someone logs out and a friend logs in on the same phone.
            existing.UserId = CurrentUserId;
            existing.Platform = request.Platform;
        }
        else
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = CurrentUserId,
                Token = request.Token,
                Platform = request.Platform,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
