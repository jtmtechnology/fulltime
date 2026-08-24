using FullTime.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record LeaderboardEntryDto(Guid UserId, string Name, decimal Balance, decimal Profit);

[ApiController]
[Route("api/leaderboard")]
[Authorize]
public class LeaderboardController(AppDbContext db) : ControllerBase
{
    // "Worldwide" isn't its own bankroll — every bet is placed in a specific league now, so this is
    // a computed average of a user's balance across whichever leagues they're in (not a sum, so
    // being in more leagues doesn't just inflate the number). A user with no leagues has nothing to
    // average and simply doesn't appear until they join one.
    [HttpGet]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetLeaderboard(CancellationToken ct)
    {
        // Both averages must come from the same group - averaging balance against a separately
        // re-read *current* StartingBalance config broke this exactly like the per-league
        // leaderboard did (confirmed happening: a global StartingBalance change retroactively
        // inflated everyone's profit, or masked a real loss as a fake profit).
        var entries = await db.LeagueMemberships
            .GroupBy(m => new { m.UserId, m.User!.Name })
            .Select(g => new
            {
                g.Key.UserId, g.Key.Name,
                AverageBalance = g.Average(m => m.Balance),
                AverageStartingBalance = g.Average(m => m.StartingBalance),
            })
            .OrderByDescending(e => e.AverageBalance)
            .Take(50)
            .Select(e => new LeaderboardEntryDto(e.UserId, e.Name, e.AverageBalance, e.AverageBalance - e.AverageStartingBalance))
            .ToListAsync(ct);

        return Ok(entries);
    }
}
