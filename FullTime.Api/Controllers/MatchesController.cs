using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.OddsApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Controllers;

public record UpcomingMatchDto(
    Guid Id,
    long LeagueId,
    string HomeTeam,
    string AwayTeam,
    string? HomeLogoUrl,
    string? AwayLogoUrl,
    DateTime KickoffTime,
    string Status,
    int? HomeScore,
    int? AwayScore,
    decimal? HomeOdds,
    decimal? DrawOdds,
    decimal? AwayOdds,
    string? Bookmaker,
    string? BookmakerLogoUrl);

[ApiController]
[Route("api/matches")]
public class MatchesController(AppDbContext db) : ControllerBase
{
    // Pure DB read regardless of the date/league filter — MatchSyncBackgroundService's timer is the
    // only thing that calls the external odds/scores provider. A date outside its synced window
    // (today .. today+DaysAhead-1) will simply come back empty rather than triggering a fetch.
    [HttpGet("upcoming")]
    public async Task<ActionResult<List<UpcomingMatchDto>>> GetUpcoming(
        [FromQuery] DateOnly? date, [FromQuery] long? leagueId, CancellationToken ct)
    {
        IQueryable<Models.Match> query;

        if (date is { } selectedDate)
        {
            var start = selectedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddDays(1);
            // A specific date was asked for explicitly, so show whatever's there regardless of status
            // (finished results for a past date are just as relevant as fixtures for a future one).
            query = db.Matches.Where(m => m.KickoffTime >= start && m.KickoffTime < end);
        }
        else
        {
            query = db.Matches.Where(m => m.Status == MatchStatus.Upcoming || m.Status == MatchStatus.InProgress);
        }

        if (leagueId is { } selectedLeagueId)
        {
            query = query.Where(m => m.LeagueId == selectedLeagueId);
        }

        var matches = await query
            .OrderBy(m => m.KickoffTime)
            .ThenBy(m => m.HomeTeam)
            .Select(m => new UpcomingMatchDto(
                m.Id,
                m.LeagueId,
                m.HomeTeam,
                m.AwayTeam,
                m.HomeTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{m.HomeTeamId}_large.png" : null,
                m.AwayTeamId > 0 ? $"https://images.fotmob.com/image_resources/logo/teamlogo/{m.AwayTeamId}_large.png" : null,
                m.KickoffTime,
                m.Status.ToString(),
                m.HomeScore,
                m.AwayScore,
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.HomeOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.DrawOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.AwayOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.Bookmaker).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.BookmakerLogoUrl).FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(matches);
    }
}
