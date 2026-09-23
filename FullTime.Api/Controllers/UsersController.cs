using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Data;
using FullTime.Api.Localization;
using FullTime.Api.Moderation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record MeDto(Guid Id, string Name, string Email, bool EmailVerified, decimal Balance, DateTime CreatedAt,
    string? Country, string CurrencySymbol);
public record UpdateProfileRequest(string Name, string? Country);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record DeleteAccountRequest(string Password);

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

        return Ok(ToMeDto(user));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        if (ProfanityFilter.ContainsProfanity(request.Name))
        {
            return BadRequest(new { error = "That name isn't allowed — please choose another." });
        }

        var user = await db.Users.FindAsync([CurrentUserId], ct);
        if (user is null) return NotFound();

        user.Name = request.Name;
        user.Country = request.Country;
        await db.SaveChangesAsync(ct);

        return Ok(ToMeDto(user));
    }

    private static MeDto ToMeDto(FullTime.Api.Models.User user) => new(
        user.Id, user.Name, user.Email, user.EmailVerified, user.Balance, user.CreatedAt,
        user.Country, CurrencyCatalog.SymbolFor(user.Country));

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

    // Apple's App Review guideline 5.1.1(v) requires in-app account deletion for any app that lets
    // people sign up. Every user-owned table cascades off Users, so deleting the row does most of
    // the work - except Leagues.CreatedByUserId, whose cascade would wipe a whole league (and every
    // other member's standing in it) just because its creator left.
    [HttpPost("me/delete")]
    public async Task<IActionResult> DeleteMe([FromBody] DeleteAccountRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId;
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Password is incorrect." });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Explicitly first: Bets.LeagueId is Restrict, so the user's own league bets have to be gone
        // before any league they solely own can be deleted below.
        await db.Bets.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);

        var ownedLeagueIds = await db.Leagues
            .Where(l => l.CreatedByUserId == userId)
            .Select(l => l.Id)
            .ToListAsync(ct);

        foreach (var leagueId in ownedLeagueIds)
        {
            var heir = await db.LeagueMemberships
                .Where(m => m.LeagueId == leagueId && m.UserId != userId)
                .OrderBy(m => m.JoinedAt)
                .Select(m => (Guid?)m.UserId)
                .FirstOrDefaultAsync(ct)
                // A league with no members left can still hold settled bets from people who've since
                // left it - Restrict blocks deleting it, so hand it to one of them instead.
                ?? await db.Bets
                    .Where(b => b.LeagueId == leagueId)
                    .Select(b => (Guid?)b.UserId)
                    .FirstOrDefaultAsync(ct);

            if (heir is { } heirId)
            {
                await db.Leagues.Where(l => l.Id == leagueId)
                    .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedByUserId, heirId), ct);
            }
            else
            {
                await db.Leagues.Where(l => l.Id == leagueId).ExecuteDeleteAsync(ct);
            }
        }

        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);

        return NoContent();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
