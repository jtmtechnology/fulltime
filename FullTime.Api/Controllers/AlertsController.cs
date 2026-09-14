using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record AlertPreferencesDto(bool LineupsOut, bool Kickoff, bool HalfTime, bool Goal, bool RedCard, bool FullTime);
public record AlertTeamDto(long TeamId, string TeamName, string? LogoUrl, long LeagueId);
// IncludedMatchIds/ExcludedMatchIds are the two explicit-override states a match can carry (see
// MatchAlertSubscription.Included) - a match absent from both just follows whatever
// FavouriteTeamIds/FavouriteLeagueIds would otherwise decide.
public record AlertSubscriptionsDto(List<long> FavouriteTeamIds, List<long> FavouriteLeagueIds, List<Guid> IncludedMatchIds, List<Guid> ExcludedMatchIds);

[ApiController]
[Route("api/alerts")]
[Authorize]
public class AlertsController(AppDbContext db) : ControllerBase
{
    [HttpGet("preferences")]
    public async Task<ActionResult<AlertPreferencesDto>> GetPreferences(CancellationToken ct)
    {
        var prefs = await GetOrCreatePreferencesAsync(ct);
        return Ok(ToDto(prefs));
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<AlertPreferencesDto>> UpdatePreferences([FromBody] AlertPreferencesDto request, CancellationToken ct)
    {
        var prefs = await GetOrCreatePreferencesAsync(ct);
        prefs.LineupsOut = request.LineupsOut;
        prefs.Kickoff = request.Kickoff;
        prefs.HalfTime = request.HalfTime;
        prefs.Goal = request.Goal;
        prefs.RedCard = request.RedCard;
        prefs.FullTime = request.FullTime;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(prefs));
    }

    // Sourced entirely from our own already-synced Matches table - no external API-Football call
    // needed, unlike LeagueCatalog's league list (which is a static, hand-maintained catalog since
    // there are only ever ~14 tracked leagues, small enough to not need a picker query at all).
    // Scoped to anything not too stale so a team that dropped out of a tracked league a season ago
    // doesn't linger in the picker forever.
    [HttpGet("teams")]
    public async Task<ActionResult<List<AlertTeamDto>>> GetTeams(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var recentMatches = db.Matches.Where(m => m.Status != MatchStatus.Postponed && m.KickoffTime >= cutoff);

        var homeTeams = recentMatches.Select(m => new { Id = m.HomeTeamId, Name = m.HomeTeam, Logo = m.HomeTeamLogoUrl, m.LeagueId, m.KickoffTime });
        var awayTeams = recentMatches.Select(m => new { Id = m.AwayTeamId, Name = m.AwayTeam, Logo = m.AwayTeamLogoUrl, m.LeagueId, m.KickoffTime });

        var teams = await homeTeams.Union(awayTeams).ToListAsync(ct);
        var distinctTeams = teams
            // Grouped by name, not TeamId - the same real club can carry more than one TeamId across
            // this app's several Highlightly/API-Football provider cutovers (see HANDOVER.md), so an
            // older match synced under a since-retired ID and a newer one under the current ID both
            // surfaced here as separate "duplicate" rows under the identical name until this fix.
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            // The most recently-seen TeamId is the one new matches will actually carry going
            // forward (API-Football is the sole live provider now) - picking that one means
            // favouriting this team matches future fixtures, not a dead historical ID.
            .Select(g => g.OrderByDescending(t => t.KickoffTime).First())
            .OrderBy(t => t.Name)
            .Select(t => new AlertTeamDto(t.Id, t.Name, t.Logo, t.LeagueId))
            .ToList();

        return Ok(distinctTeams);
    }

    // One combined payload rather than three separate calls - MatchCard needs to check "is this
    // match's bell lit" for every card on a page in one shot, not one request per card.
    [HttpGet("subscriptions")]
    public async Task<ActionResult<AlertSubscriptionsDto>> GetSubscriptions(CancellationToken ct)
    {
        var teamIds = await db.FavouriteTeams.Where(f => f.UserId == CurrentUserId).Select(f => f.TeamId).ToListAsync(ct);
        var leagueIds = await db.FavouriteLeagues.Where(f => f.UserId == CurrentUserId).Select(f => f.LeagueId).ToListAsync(ct);
        var includedMatchIds = await db.MatchAlertSubscriptions.Where(s => s.UserId == CurrentUserId && s.Included).Select(s => s.MatchId).ToListAsync(ct);
        var excludedMatchIds = await db.MatchAlertSubscriptions.Where(s => s.UserId == CurrentUserId && !s.Included).Select(s => s.MatchId).ToListAsync(ct);
        return Ok(new AlertSubscriptionsDto(teamIds, leagueIds, includedMatchIds, excludedMatchIds));
    }

    [HttpPost("favourite-teams/{teamId:long}")]
    public async Task<IActionResult> AddFavouriteTeam(long teamId, CancellationToken ct)
    {
        if (!await db.FavouriteTeams.AnyAsync(f => f.UserId == CurrentUserId && f.TeamId == teamId, ct))
        {
            db.FavouriteTeams.Add(new FavouriteTeam { Id = Guid.NewGuid(), UserId = CurrentUserId, TeamId = teamId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        return Ok();
    }

    [HttpDelete("favourite-teams/{teamId:long}")]
    public async Task<IActionResult> RemoveFavouriteTeam(long teamId, CancellationToken ct)
    {
        await db.FavouriteTeams.Where(f => f.UserId == CurrentUserId && f.TeamId == teamId).ExecuteDeleteAsync(ct);
        return Ok();
    }

    [HttpPost("favourite-leagues/{leagueId:long}")]
    public async Task<IActionResult> AddFavouriteLeague(long leagueId, CancellationToken ct)
    {
        if (!await db.FavouriteLeagues.AnyAsync(f => f.UserId == CurrentUserId && f.LeagueId == leagueId, ct))
        {
            db.FavouriteLeagues.Add(new FavouriteLeague { Id = Guid.NewGuid(), UserId = CurrentUserId, LeagueId = leagueId, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        return Ok();
    }

    [HttpDelete("favourite-leagues/{leagueId:long}")]
    public async Task<IActionResult> RemoveFavouriteLeague(long leagueId, CancellationToken ct)
    {
        await db.FavouriteLeagues.Where(f => f.UserId == CurrentUserId && f.LeagueId == leagueId).ExecuteDeleteAsync(ct);
        return Ok();
    }

    // Backs MatchCard's bell icon directly - an explicit override in either direction, not a plain
    // add/remove. POST forces alerts on (a match doesn't need to belong to a favourite team/league
    // at all for someone to want just that one game); DELETE forces them off, which matters even
    // when the match DOES involve a favourite - otherwise the only way to silence one specific game
    // would be unfavouriting its whole team. See MatchAlertSubscription.Included.
    [HttpPost("matches/{matchId:guid}")]
    public Task<IActionResult> AddMatchSubscription(Guid matchId, CancellationToken ct) => SetMatchOverrideAsync(matchId, included: true, ct);

    [HttpDelete("matches/{matchId:guid}")]
    public Task<IActionResult> RemoveMatchSubscription(Guid matchId, CancellationToken ct) => SetMatchOverrideAsync(matchId, included: false, ct);

    private async Task<IActionResult> SetMatchOverrideAsync(Guid matchId, bool included, CancellationToken ct)
    {
        if (!await db.Matches.AnyAsync(m => m.Id == matchId, ct))
        {
            return NotFound();
        }

        var existing = await db.MatchAlertSubscriptions.FirstOrDefaultAsync(s => s.UserId == CurrentUserId && s.MatchId == matchId, ct);
        if (existing is null)
        {
            db.MatchAlertSubscriptions.Add(new MatchAlertSubscription
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, MatchId = matchId, Included = included, CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Included = included;
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    private async Task<UserAlertPreferences> GetOrCreatePreferencesAsync(CancellationToken ct)
    {
        var prefs = await db.UserAlertPreferences.FirstOrDefaultAsync(p => p.UserId == CurrentUserId, ct);
        if (prefs is null)
        {
            prefs = new UserAlertPreferences { Id = Guid.NewGuid(), UserId = CurrentUserId };
            db.UserAlertPreferences.Add(prefs);
            await db.SaveChangesAsync(ct);
        }

        return prefs;
    }

    private static AlertPreferencesDto ToDto(UserAlertPreferences p) =>
        new(p.LineupsOut, p.Kickoff, p.HalfTime, p.Goal, p.RedCard, p.FullTime);

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
