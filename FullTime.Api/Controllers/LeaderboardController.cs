using FullTime.Api.Betting;
using FullTime.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Controllers;

public record LeaderboardEntryDto(Guid UserId, string Name, decimal Balance, decimal Profit);

[ApiController]
[Route("api/leaderboard")]
[Authorize]
public class LeaderboardController(AppDbContext db, IOptions<BettingOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetLeaderboard(CancellationToken ct)
    {
        var startingBalance = options.Value.StartingBalance;

        var entries = await db.Users
            .OrderByDescending(u => u.Balance)
            .Take(50)
            .Select(u => new LeaderboardEntryDto(u.Id, u.Name, u.Balance, u.Balance - startingBalance))
            .ToListAsync(ct);

        return Ok(entries);
    }
}
