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
    bool BetBuilderAvailable,
    bool EventsAvailable);

public record BetBuilderMarketDto(
    string MarketType, decimal? Line, string? Side, int? PredictedHomeScore, int? PredictedAwayScore, decimal Price,
    string? PlayerName = null, string? Team = null);
public record BetBuilderMarketsResponse(bool Available, List<BetBuilderMarketDto> Markets, string? Bookmaker, string? BookmakerLogoUrl);

public record MatchEventDto(
    string Team, string Minute, string Type, string? PlayerName, string? AssistPlayerName, string? SubstitutedPlayerName);

[ApiController]
[Route("api/matches")]
public class MatchesController(
    AppDbContext db,
    IOptions<HighlightlyOptions> highlightlyOptions,
    IOptions<ProvidersOptions> providersOptions,
    OddsApiMarketService oddsApiMarkets,
    IServiceScopeFactory scopeFactory,
    ILogger<MatchesController> logger) : ControllerBase
{
    // Pure DB read for Highlightly (its background sync is the only thing that calls the provider).
    // For the-odds-api, h2h freshness is kicked off on-demand but NOT awaited — this endpoint always
    // returns whatever's already cached immediately, refreshing in the background for the *next*
    // request to pick up. Confirmed in production 2026-09-06 that awaiting it inline made selecting
    // a day noticeably slow: each match needing a refresh is a real external call serialized through
    // OddsApiClient's rate-limit throttle (added the same day to stop real 429s under heavy
    // concurrent load), so a day with several stale matches meant several seconds of blocking before
    // the page could respond at all. Still "on demand, triggered by an actual view" per the standing
    // no-background-polling preference - it's just not this specific request's problem to wait on.
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
            var candidateMatchIds = await query.Select(m => m.Id).ToListAsync(ct);
            TriggerH2hRefreshInBackground(candidateMatchIds);
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
                m.BetBuilderMarkets.Any(),
                m.Events.Any()))
            .ToListAsync(ct);

        return Ok(matches);
    }

    // Runs on a detached scope/CancellationToken since the request's own db/ct are disposed and
    // cancelled the moment this HTTP response is sent - the whole point is this outlives the
    // request that triggered it. Best-effort: a failure here just means the next view of this date
    // tries again, same as any other on-demand cache miss.
    private void TriggerH2hRefreshInBackground(List<Guid> matchIds)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedOddsApiMarkets = scope.ServiceProvider.GetRequiredService<OddsApiMarketService>();
                var matches = await scopedDb.Matches.Where(m => matchIds.Contains(m.Id)).ToListAsync();
                await scopedOddsApiMarkets.EnsureH2hFreshAsync(matches, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background h2h refresh failed for {Count} match(es)", matchIds.Count);
            }
        });
    }

    // Same detached-scope reasoning as TriggerH2hRefreshInBackground above.
    private void TriggerPlayerPropsRefreshInBackground(Guid matchId)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            try
            {
                var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var scopedPlayerProps = scope.ServiceProvider.GetRequiredService<PlayerPropsService>();
                var match = await scopedDb.Matches.FindAsync([matchId]);
                if (match is not null)
                {
                    await scopedPlayerProps.EnsureFreshAsync(match, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background player-props refresh failed for match {MatchId}", matchId);
            }
        });
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

        // Independent of MarketsSource - EPL player props run regardless of whether Highlightly or
        // OddsApi is the active markets source, since Highlightly has no player-prop markets of its
        // own (see PlayerPropsService). NOT awaited: same reasoning as TriggerH2hRefreshInBackground
        // below - most requests are a cheap DB-only no-op (everything already found, or still within
        // the hourly retry window), but the rare real fetch is several serialized the-odds-api calls
        // that made this endpoint noticeably slow when awaited inline. Returns whatever's already
        // cached immediately; a fresh fetch (if one fires) is picked up by the next view instead.
        TriggerPlayerPropsRefreshInBackground(id);

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

    // Pure DB read - populated by BetBuilderSyncService.ResolveMatchEventsAsync once a match
    // finishes, never fetched on demand (no live provider call happens from this endpoint at all).
    // Ordered in memory since Minute is stored as Highlightly's raw string ("45+2") - a DB-level
    // string sort would put "9" after "45"/"70".
    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<List<MatchEventDto>>> GetMatchEvents(Guid id, CancellationToken ct)
    {
        var events = await db.MatchEvents
            .Where(e => e.MatchId == id)
            .Select(e => new MatchEventDto(
                e.Team.ToString(), e.Minute, e.Type, e.PlayerName, e.AssistPlayerName, e.SubstitutedPlayerName))
            .ToListAsync(ct);

        return Ok(events.OrderBy(e => ParseMinute(e.Minute)).ToList());
    }

    // Same stoppage-time-aware parsing as BetBuilderSyncService.ParseMinute - "45+2" sorts right
    // after "45" and before "46", not lexicographically before "9".
    private static (int Base, int Added) ParseMinute(string time)
    {
        var parts = time.Split('+');
        var baseMinute = int.TryParse(parts[0], out var b) ? b : int.MaxValue;
        var added = parts.Length > 1 && int.TryParse(parts[1], out var a) ? a : 0;
        return (baseMinute, added);
    }
}
