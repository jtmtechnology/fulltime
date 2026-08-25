using FullTime.Api.Data;
using FullTime.Api.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record LeaderboardEntryDto(Guid UserId, string Name, string LeagueName, decimal Balance, decimal Profit, string CurrencySymbol);

[ApiController]
[Route("api/leaderboard")]
[Authorize]
public class LeaderboardController(AppDbContext db) : ControllerBase
{
    // "Worldwide" isn't its own bankroll — every bet is placed in a specific league now, so this is
    // every membership across every league, ranked by that membership's own profit. Deliberately not
    // collapsed to one row per user any more - a member of three leagues shows up three times, once
    // per league, since a league's own name and profit are what's being ranked, not an average that
    // hides which league actually produced the result.
    [HttpGet]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetLeaderboard(CancellationToken ct)
    {
        var rows = await db.LeagueMemberships
            .OrderByDescending(m => m.Balance - m.StartingBalance)
            .Take(50)
            .Select(m => new
            {
                m.UserId, m.User!.Name, m.User!.Country, LeagueName = m.League!.Name,
                m.Balance, Profit = m.Balance - m.StartingBalance,
            })
            .ToListAsync(ct);

        var entries = rows.Select(r => new LeaderboardEntryDto(
            r.UserId, r.Name, r.LeagueName, r.Balance, r.Profit, CurrencyCatalog.SymbolFor(r.Country))).ToList();

        return Ok(entries);
    }
}
