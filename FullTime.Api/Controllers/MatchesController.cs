using FullTime.Api.BetBuilder;
using FullTime.Api.BetBuilder.OddsApi;
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
    int? Minute,
    bool IsHalfTime,
    decimal? HomeOdds,
    decimal? DrawOdds,
    decimal? AwayOdds,
    string? Bookmaker,
    string? BookmakerLogoUrl,
    bool BetBuilderAvailable);

public record BetBuilderMarketDto(
    string MarketType, decimal? Line, string? Side, int? PredictedHomeScore, int? PredictedAwayScore, decimal Price,
    string? PlayerName = null, string? Team = null);
public record BetBuilderMarketsResponse(bool Available, List<BetBuilderMarketDto> Markets, string? Bookmaker, string? BookmakerLogoUrl);

[ApiController]
[Route("api/matches")]
public class MatchesController(
    AppDbContext db,
    IOptions<HighlightlyOptions> highlightlyOptions,
    IOptions<ProvidersOptions> providersOptions,
    OddsApiMarketService oddsApiMarkets) : ControllerBase
{
    // Pure DB read for Highlightly (its background sync is the only thing that calls the provider),
    // but ensures h2h freshness on-demand first when Providers:MarketsSource == "OddsApi" — scoped
    // to just the matches this specific call is about to return, not every tracked match, per the
    // standing no-background-polling preference. A date outside the synced window will simply come
    // back empty rather than triggering a fetch.
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

        if (providersOptions.Value.MarketsSource == "OddsApi")
        {
            var candidateMatches = await query.ToListAsync(ct);
            await oddsApiMarkets.EnsureH2hFreshAsync(candidateMatches, ct);
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
                m.Minute,
                m.IsHalfTime,
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.HomeOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.DrawOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => (decimal?)o.AwayOdds).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.Bookmaker).FirstOrDefault(),
                m.OddsSnapshots.OrderByDescending(o => o.FetchedAt).Select(o => o.BookmakerLogoUrl).FirstOrDefault(),
                m.BetBuilderMarkets.Any()))
            .ToListAsync(ct);

        return Ok(matches);
    }

    // For Highlightly, reads whatever BetBuilderSyncBackgroundService's timer has already stored.
    // For the-odds-api, ensures a fresh full 9-market pull first (on-demand, cache-miss only — see
    // OddsApiMarketService) since there's no background sync for this any more. Most matches will
    // still come back Available: false either way, since neither provider prices every fixture. The
    // client uses this to decide whether to offer the Bet Builder entry point at all.
    [HttpGet("{id:guid}/bet-builder-markets")]
    public async Task<ActionResult<BetBuilderMarketsResponse>> GetBetBuilderMarkets(Guid id, CancellationToken ct)
    {
        string? bookmaker;
        string? bookmakerLogoUrl;

        if (providersOptions.Value.MarketsSource == "OddsApi")
        {
            var match = await db.Matches.FindAsync([id], ct);
            if (match is not null)
            {
                await oddsApiMarkets.EnsureBetBuilderMarketsFreshAsync(match, ct);
            }

            // the-odds-api aggregates several bookmakers; the h2h snapshot (fetched in the same
            // pull) already records which one was actually used for this match, so that's reused
            // here as a single consistent label for the whole response rather than a fixed config
            // value like Highlightly's BookmakerName.
            var latestSnapshot = await db.OddsSnapshots
                .Where(o => o.MatchId == id).OrderByDescending(o => o.FetchedAt).FirstOrDefaultAsync(ct);
            bookmaker = latestSnapshot?.Bookmaker;
            bookmakerLogoUrl = latestSnapshot?.BookmakerLogoUrl;
        }
        else
        {
            bookmaker = highlightlyOptions.Value.BookmakerName;
            bookmakerLogoUrl = BookmakerLogos.UrlFor(bookmaker);
        }

        var markets = await db.BetBuilderMarkets
            .Where(m => m.MatchId == id)
            .OrderBy(m => m.MarketType)
            .ThenBy(m => m.Line)
            .ThenBy(m => m.PredictedHomeScore)
            .ThenBy(m => m.PredictedAwayScore)
            .Select(m => new BetBuilderMarketDto(
                m.MarketType.ToString(), m.Line, m.Side.HasValue ? m.Side.ToString() : null,
                m.PredictedHomeScore, m.PredictedAwayScore, m.Price, m.PlayerName, m.Team))
            .ToListAsync(ct);

        return Ok(new BetBuilderMarketsResponse(markets.Count > 0, markets, bookmaker, bookmakerLogoUrl));
    }
}
