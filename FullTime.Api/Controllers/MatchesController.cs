using FullTime.Api.BetBuilder;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    string? BookmakerLogoUrl,
    bool BetBuilderAvailable);

public record BetBuilderMarketDto(
    string MarketType, decimal? Line, string? Side, int? PredictedHomeScore, int? PredictedAwayScore, decimal Price);
public record BetBuilderMarketsResponse(bool Available, List<BetBuilderMarketDto> Markets, string? Bookmaker, string? BookmakerLogoUrl);

[ApiController]
[Route("api/matches")]
public class MatchesController(AppDbContext db, IOptions<HighlightlyOptions> highlightlyOptions) : ControllerBase
{
    // Pure DB read regardless of the date/league filter — HighlightlyMatchSyncBackgroundService's
    // timer is the only thing that calls the external odds/scores provider. A date outside its
    // synced window (today .. today+MatchSyncDaysAhead-1) will simply come back empty rather than
    // triggering a fetch.
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
                m.HomeTeamLogoUrl,
                m.AwayTeamLogoUrl,
                m.KickoffTime,
                m.Status.ToString(),
                m.HomeScore,
                m.AwayScore,
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.HomeOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.DrawOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.AwayOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.Bookmaker).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.BookmakerLogoUrl).FirstOrDefault(),
                m.BetBuilderMarkets.Any()))
            .ToListAsync(ct);

        return Ok(matches);
    }

    // Reads whatever BetBuilderSyncBackgroundService's timer has already stored — most matches will
    // come back Available: false, since the odds-feed provider only prices the next gameweek or so
    // per league. The client uses this to decide whether to offer the Bet Builder entry point at all.
    [HttpGet("{id:guid}/bet-builder-markets")]
    public async Task<ActionResult<BetBuilderMarketsResponse>> GetBetBuilderMarkets(Guid id, CancellationToken ct)
    {
        var markets = await db.BetBuilderMarkets
            .Where(m => m.MatchId == id)
            .OrderBy(m => m.MarketType)
            .ThenBy(m => m.Line)
            .ThenBy(m => m.PredictedHomeScore)
            .ThenBy(m => m.PredictedAwayScore)
            .Select(m => new BetBuilderMarketDto(
                m.MarketType.ToString(), m.Line, m.Side.HasValue ? m.Side.ToString() : null,
                m.PredictedHomeScore, m.PredictedAwayScore, m.Price))
            .ToListAsync(ct);

        var bookmaker = highlightlyOptions.Value.BookmakerName;
        var logoUrl = BookmakerLogos.UrlFor(bookmaker);

        return Ok(new BetBuilderMarketsResponse(markets.Count > 0, markets, bookmaker, logoUrl));
    }
}
