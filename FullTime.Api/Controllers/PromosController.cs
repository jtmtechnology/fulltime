using FullTime.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record PromoDto(Guid Id, string Title, string? Subtitle, string? Icon, string Theme, string? Link);

[ApiController]
[Route("api/promos")]
[Authorize]
public class PromosController(AppDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<List<PromoDto>>> GetActive(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var promos = await db.Promos
            .Where(p => p.IsActive && (p.StartsAt == null || p.StartsAt <= now) && (p.EndsAt == null || p.EndsAt > now))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

        // Links are dropped rather than passed through unless they're an in-app route - an external
        // URL (a bookmaker, say) would undercut the app's no-real-money scoping and App Review.
        // "//host" is a protocol-relative external URL, so it doesn't count as in-app.
        return Ok(promos.Select(p => new PromoDto(
            p.Id, p.Title, p.Subtitle, p.Icon, p.Theme,
            p.Link is { } link && link.StartsWith('/') && !link.StartsWith("//") ? link : null)).ToList());
    }
}
