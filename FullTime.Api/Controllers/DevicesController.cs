using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FullTime.Api.Controllers;

public record RegisterDeviceRequest(string Token, string Platform, int? UtcOffsetMinutes = null);
public record UnregisterDeviceRequest(string Token);

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

        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform))
        {
            return BadRequest(new { error = $"Invalid platform '{request.Platform}' — expected Android or iOS." });
        }

        if (request.UtcOffsetMinutes is { } offset)
        {
            var user = await db.Users.FindAsync([CurrentUserId], ct);
            if (user is not null)
            {
                user.UtcOffsetMinutes = offset;
            }
        }

        var existing = await db.DeviceTokens.SingleOrDefaultAsync(d => d.Token == request.Token, ct);
        if (existing is not null)
        {
            // The same physical device token can end up registered against a different account
            // if someone logs out and a friend logs in on the same phone.
            existing.UserId = CurrentUserId;
            existing.Platform = platform;
        }
        else
        {
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(),
                UserId = CurrentUserId,
                Token = request.Token,
                Platform = platform,
                CreatedAt = DateTime.UtcNow,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent registration call for the same token — the app's push
            // library can fire more than one registration in quick succession (e.g. an explicit
            // check right after a TokenRefreshed event for that same fresh token). The other call
            // already inserted the row; just make sure it points at this request's user/platform.
            db.ChangeTracker.Clear();
            var winner = await db.DeviceTokens.SingleAsync(d => d.Token == request.Token, ct);
            winner.UserId = CurrentUserId;
            winner.Platform = platform;
            await db.SaveChangesAsync(ct);
        }

        return Ok();
    }

    // Called on logout, while the JWT is still valid - otherwise the phone keeps receiving the
    // logged-out user's pushes until someone else happens to log in and re-claim the token.
    [HttpPost("unregister")]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDeviceRequest request, CancellationToken ct)
    {
        await db.DeviceTokens
            .Where(d => d.Token == request.Token && d.UserId == CurrentUserId)
            .ExecuteDeleteAsync(ct);

        return NoContent();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
