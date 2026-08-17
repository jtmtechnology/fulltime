using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Betting;
using FullTime.Api.Data;
using FullTime.Api.Leagues;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Controllers;

public record CreateLeagueRequest(string Name);
public record JoinLeagueRequest(string InviteCode);
public record LeagueSummaryDto(
    Guid Id, string Name, string InviteCode, int MemberCount, DateTime CreatedAt, bool IsOwner,
    decimal Balance, decimal Profit);

[ApiController]
[Route("api/leagues")]
[Authorize]
public class LeaguesController(AppDbContext db, LeagueService leagueService, IOptions<BettingOptions> options) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<LeagueSummaryDto>> CreateLeague([FromBody] CreateLeagueRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "League name is required." });
        }

        var startingBalance = options.Value.StartingBalance;
        var league = await leagueService.CreateAsync(CurrentUserId, request.Name.Trim(), ct);

        return Created(string.Empty, new LeagueSummaryDto(
            league.Id, league.Name, league.InviteCode, 1, league.CreatedAt, true, startingBalance, 0));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<LeagueSummaryDto>>> GetMyLeagues(CancellationToken ct)
    {
        var startingBalance = options.Value.StartingBalance;
        var userId = CurrentUserId;

        // Filter the specific membership by CurrentUserId inside the projection — filtering the
        // league by "any member is me" and then taking .First() off the whole list would silently
        // show whichever member happens to sort first, not necessarily the caller.
        var leagues = await db.Leagues
            .Where(l => l.Memberships.Any(m => m.UserId == userId))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new
            {
                l.Id, l.Name, l.InviteCode, l.CreatedAt, l.CreatedByUserId,
                MemberCount = l.Memberships.Count,
                MyBalance = l.Memberships.First(m => m.UserId == userId).Balance,
            })
            .ToListAsync(ct);

        return Ok(leagues.Select(l => new LeagueSummaryDto(
            l.Id, l.Name, l.InviteCode, l.MemberCount, l.CreatedAt, l.CreatedByUserId == userId,
            l.MyBalance, l.MyBalance - startingBalance)));
    }

    [HttpPost("join")]
    public async Task<ActionResult<LeagueSummaryDto>> JoinLeague([FromBody] JoinLeagueRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InviteCode))
        {
            return BadRequest(new { error = "Invite code is required." });
        }

        var startingBalance = options.Value.StartingBalance;
        var result = await leagueService.JoinAsync(CurrentUserId, request.InviteCode, ct);

        if (result.Outcome != JoinLeagueOutcome.Success)
        {
            var error = result.Outcome switch
            {
                JoinLeagueOutcome.InvalidCode => "That invite code doesn't match any league.",
                JoinLeagueOutcome.AlreadyMember => "You're already in this league.",
                _ => "Could not join league.",
            };
            return BadRequest(new { error });
        }

        var league = result.League!;
        var memberCount = await db.LeagueMemberships.CountAsync(m => m.LeagueId == league.Id, ct);

        return Ok(new LeagueSummaryDto(
            league.Id, league.Name, league.InviteCode, memberCount, league.CreatedAt,
            league.CreatedByUserId == CurrentUserId, startingBalance, 0));
    }

    [HttpGet("{id:guid}/leaderboard")]
    public async Task<ActionResult<List<LeaderboardEntryDto>>> GetLeagueLeaderboard(Guid id, CancellationToken ct)
    {
        var isMember = await db.LeagueMemberships.AnyAsync(m => m.LeagueId == id && m.UserId == CurrentUserId, ct);
        if (!isMember)
        {
            return NotFound(new { error = "League not found." });
        }

        var startingBalance = options.Value.StartingBalance;

        var entries = await db.LeagueMemberships
            .Where(m => m.LeagueId == id)
            .Include(m => m.User)
            .OrderByDescending(m => m.Balance)
            .Select(m => new LeaderboardEntryDto(m.UserId, m.User!.Name, m.Balance, m.Balance - startingBalance))
            .ToListAsync(ct);

        return Ok(entries);
    }

    [HttpDelete("{id:guid}/membership")]
    public async Task<IActionResult> LeaveLeague(Guid id, CancellationToken ct)
    {
        var membership = await db.LeagueMemberships
            .FirstOrDefaultAsync(m => m.LeagueId == id && m.UserId == CurrentUserId, ct);
        if (membership is null)
        {
            return NotFound(new { error = "You're not a member of this league." });
        }

        // Its stake is already debited from this league's balance — if it later resolves Won with
        // the membership gone, settlement has nowhere to credit the winnings. Block until it settles.
        var hasPendingBet = await db.Bets.AnyAsync(
            b => b.LeagueId == id && b.UserId == CurrentUserId && b.Status == BetStatus.Pending, ct);
        if (hasPendingBet)
        {
            return BadRequest(new { error = "You have pending bets in this league — wait for them to settle before leaving." });
        }

        db.LeagueMemberships.Remove(membership);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
