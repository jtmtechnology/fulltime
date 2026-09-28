using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record AdminPromoDto(
    Guid Id, string Title, string? Subtitle, string? Icon, string Theme, string? Link,
    DateTime? StartsAt, DateTime? EndsAt, int Priority, bool IsActive, DateTime CreatedAt);

public record SavePromoRequest(
    string Title, string? Subtitle, string? Icon, string? Theme, string? Link,
    DateTime? StartsAt, DateTime? EndsAt, int Priority, bool IsActive);

// Backs wwwroot/admin/promos.html. Unlike PromosController this returns every row, including
// inactive and expired ones, so they can be edited or re-enabled.
[ApiController]
[Route("api/admin/promos")]
[Authorize(Policy = "Admin")]
public class AdminPromosController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AdminPromoDto>>> GetAll(CancellationToken ct)
    {
        var promos = await db.Promos
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.Priority)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);
        return Ok(promos.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<AdminPromoDto>> Create(SavePromoRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } error)
        {
            return BadRequest(new { error });
        }

        var promo = new Promo { Id = Guid.NewGuid(), Title = "", CreatedAt = DateTime.UtcNow };
        Apply(promo, request);
        db.Promos.Add(promo);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(promo));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminPromoDto>> Update(Guid id, SavePromoRequest request, CancellationToken ct)
    {
        var promo = await db.Promos.FindAsync([id], ct);
        if (promo is null)
        {
            return NotFound(new { error = "Promo not found." });
        }
        if (Validate(request) is { } error)
        {
            return BadRequest(new { error });
        }

        Apply(promo, request);
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(promo));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await db.Promos.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound(new { error = "Promo not found." }) : NoContent();
    }

    private static string? Validate(SavePromoRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title))
        {
            return "Title is required.";
        }
        if (r.Theme is { } theme && !Promo.Themes.Contains(theme))
        {
            return $"Theme must be one of: {string.Join(", ", Promo.Themes)}.";
        }
        if (!string.IsNullOrWhiteSpace(r.Link) && !Promo.IsInAppLink(r.Link.Trim()))
        {
            return "Link must be an in-app route starting with \"/\", e.g. /leaderboard.";
        }
        if (r.StartsAt is { } start && r.EndsAt is { } end && end <= start)
        {
            return "End time must be after the start time.";
        }
        return null;
    }

    // Blank optional fields are stored as null, not "", so the app's IsNullOrEmpty checks and the
    // API's null-means-no-link logic see the same thing.
    private static void Apply(Promo promo, SavePromoRequest r)
    {
        promo.Title = r.Title.Trim();
        promo.Subtitle = NullIfBlank(r.Subtitle);
        promo.Icon = NullIfBlank(r.Icon);
        promo.Theme = r.Theme ?? "accent";
        promo.Link = NullIfBlank(r.Link);
        promo.StartsAt = r.StartsAt?.ToUniversalTime();
        promo.EndsAt = r.EndsAt?.ToUniversalTime();
        promo.Priority = r.Priority;
        promo.IsActive = r.IsActive;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AdminPromoDto ToDto(Promo p) => new(
        p.Id, p.Title, p.Subtitle, p.Icon, p.Theme, p.Link, p.StartsAt, p.EndsAt, p.Priority, p.IsActive, p.CreatedAt);
}
