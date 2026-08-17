using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record MeDto(Guid Id, string Name, string Email, bool EmailVerified, decimal Balance, DateTime CreatedAt);
public record UpdateProfileRequest(string Name);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> GetMe(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([CurrentUserId], ct);
        if (user is null) return NotFound();

        return Ok(new MeDto(user.Id, user.Name, user.Email, user.EmailVerified, user.Balance, user.CreatedAt));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        var user = await db.Users.FindAsync([CurrentUserId], ct);
        if (user is null) return NotFound();

        user.Name = request.Name;
        await db.SaveChangesAsync(ct);

        return Ok(new MeDto(user.Id, user.Name, user.Email, user.EmailVerified, user.Balance, user.CreatedAt));
    }

    [HttpPost("me/change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
        {
            return BadRequest(new { error = "New password must be at least 8 characters." });
        }

        var user = await db.Users.FindAsync([CurrentUserId], ct);
        if (user is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Unauthorized(new { error = "Current password is incorrect." });
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await db.SaveChangesAsync(ct);

        return Ok(new { message = "Password changed." });
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
