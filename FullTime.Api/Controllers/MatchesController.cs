using FullTime.Api.BetBuilder;
using FullTime.Api.BetBuilder.ApiFootball;
using FullTime.Api.BetBuilder.Dtos;
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
    int? AddedTimeMinutes,
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
public record BetBuilderMarketsResponse(
    bool Available, List<BetBuilderMarketDto> Markets, string? Bookmaker, string? BookmakerLogoUrl,
    List<string> HomeForm, List<string> AwayForm);

public record MatchEventDto(
    string Team, string Minute, string Type, string? PlayerName, string? AssistPlayerName, string? SubstitutedPlayerName);

public record TeamStandingDto(int Position, string TeamName, string? CrestUrl, int Played, int GoalDifference, int Points);

public record MatchStatRowDto(string Label, string HomeDisplay, string AwayDisplay, double HomeValue, double AwayValue);
public record MatchStatsResponse(bool Available, List<MatchStatRowDto> Rows);

public record PlayerStatRowDto(
    long PlayerId, string Name, string? PhotoUrl, string? Position, int Minutes, bool Substitute,
    string? Rating, int Goals, int Assists, int ShotsOnTarget, int ShotsTotal, int PassesTotal,
    int? PassAccuracyPercent, int YellowCards, int RedCards);
public record MatchPlayerStatsResponse(bool Available, List<PlayerStatRowDto> Home, List<PlayerStatRowDto> Away);

[ApiController]
[Route("api/matches")]
public class MatchesController(
    AppDbContext db,
    IOptions<HighlightlyOptions> highlightlyOptions,
    IOptions<ApiFootballOptions> apiFootballOptions,
    IOptions<ProvidersOptions> providersOptions,
    OddsApiMarketService oddsApiMarkets,
    ApiFootballTeamFormService teamFormService,
    ApiFootballStandingsService standingsService,
    ApiFootballMatchStatsService matchStatsService,
    ApiFootballPlayerStatsService playerStatsService,
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
            // (finished results for a past date are just as relevant as fixtures for a future one) -
            // except Postponed, which never actually happened on this date and shouldn't be shown as
            // if it were a fixture here (per user request 2026-09-09).
            query = db.Matches.Where(m =>
                m.KickoffTime >= start && m.KickoffTime < end && m.Status != MatchStatus.Postponed);
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
                m.AddedTimeMinutes,
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
        var match = await db.Matches.FindAsync([id], ct);
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
            if (match is not null)
            {
                await oddsApiMarkets.EnsureBetBuilderMarketsFreshAsync(match, ct);
            }

            // the-odds-api aggregates several bookmakers; the h2h snapshot (fetched in the same
            // pull) already records which one was actually used for this match, so that's reused
            // here as a single consistent label for the whole response rather than a fixed config
            // value like Highlightly's/API-Football's BookmakerName.
            var latestSnapshot = await db.OddsSnapshots
                .Where(o => o.MatchId == id).OrderByDescending(o => o.FetchedAt).FirstOrDefaultAsync(ct);
            bookmaker = latestSnapshot?.Bookmaker;
            bookmakerLogoUrl = latestSnapshot?.BookmakerLogoUrl;
        }
        else if (providersOptions.Value.MarketsSource == "ApiFootball")
        {
            // Single fixed bookmaker (see ApiFootballOptions.OddsBookmakerId/BookmakerName), same
            // reasoning as Highlightly's branch below - unlike the-odds-api there's no per-match
            // "which bookmaker actually priced this" to look up, it's always the one we asked for.
            bookmaker = apiFootballOptions.Value.BookmakerName;
            bookmakerLogoUrl = BookmakerLogos.UrlFor(bookmaker);
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

        // Sourced from API-Football's own /teams/statistics "form" field (ApiFootballTeamFormService),
        // not derived from our own Matches table - gives real season-to-date form immediately instead
        // of waiting for enough matches to complete under the current provider's team-ID scheme.
        // Match.LeagueId is ALWAYS stored in Highlightly's own ID space regardless of which provider
        // is live (see HighlightlyToApiFootballLeagueMap's own comment - a deliberate fix so the
        // client's league catalog keeps working) - so it must be translated to API-Football's league
        // ID before calling out, it is never directly usable as one. Home/AwayTeamId, unlike
        // LeagueId, are NOT translated - they're only meaningfully API-Football IDs while ApiFootball
        // is actually the live-score provider, so both checks are required, not just the league map.
        List<string> homeForm = [];
        List<string> awayForm = [];
        if (match is not null && providersOptions.Value.LiveScoreSource == "ApiFootball"
            && HighlightlyToApiFootballLeagueMap.LeagueIds.TryGetValue(match.LeagueId, out var apiFootballLeagueId))
        {
            homeForm = await teamFormService.GetFormAsync(match.HomeTeamId, apiFootballLeagueId, ct);
            awayForm = await teamFormService.GetFormAsync(match.AwayTeamId, apiFootballLeagueId, ct);
        }

        return Ok(new BetBuilderMarketsResponse(markets.Count > 0, markets, bookmaker, bookmakerLogoUrl, homeForm, awayForm));
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

    // League table for the Table page. LeagueId here is Highlightly's own ID (the same space
    // Match.LeagueId/LeagueCatalog use, regardless of which provider is actually live - see
    // HighlightlyToApiFootballLeagueMap's own comment) - translated to API-Football's league ID
    // before calling out, same as GetBetBuilderMarkets' team-form lookup does. Unlike that lookup,
    // this doesn't need to gate on LiveScoreSource == "ApiFootball" first: the league-ID map is a
    // static correspondence between two ID spaces, not dependent on which provider populated any
    // particular Match row. Pure knockout competitions (no map entry meaningfully has a table) and
    // any provider miss both just return an empty list - the client renders that as "no table
    // available" rather than an error.
    [HttpGet("standings/{leagueId:long}")]
    public async Task<ActionResult<List<TeamStandingDto>>> GetStandings(long leagueId, CancellationToken ct)
    {
        if (!HighlightlyToApiFootballLeagueMap.LeagueIds.TryGetValue(leagueId, out var apiFootballLeagueId))
        {
            return Ok(new List<TeamStandingDto>());
        }

        var standings = await standingsService.GetStandingsAsync(apiFootballLeagueId, ct);
        return Ok(standings
            .Select(s => new TeamStandingDto(s.Rank, s.Team.Name, s.Team.Logo, s.All.Played, s.GoalsDiff, s.Points))
            .ToList());
    }

    // Match Summary's Stats tab. Match.ExternalId holds API-Football's own fixture ID directly (true
    // since the 2026-09-10 cutover made ApiFootball the live-score provider - see
    // ApiFootballMatchSyncService.UpsertMatchAsync - same assumption ResolveMatchEventsAsync already
    // relies on for settlement), so no league/ID-space translation is needed here unlike GetStandings.
    // Rows are built defensively per stat type - a type the provider doesn't return for this fixture
    // (e.g. expected_goals isn't priced for every competition/plan) is simply omitted rather than
    // shown as a blank/zero row.
    [HttpGet("{id:guid}/stats")]
    public async Task<ActionResult<MatchStatsResponse>> GetMatchStats(Guid id, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([id], ct);
        if (match is null || !long.TryParse(match.ExternalId, out var fixtureId))
        {
            return Ok(new MatchStatsResponse(false, []));
        }

        var teams = await matchStatsService.GetStatisticsAsync(fixtureId, ct);
        var home = teams.FirstOrDefault(t => t.Team.Id == match.HomeTeamId) ?? teams.ElementAtOrDefault(0);
        var away = teams.FirstOrDefault(t => t.Team.Id == match.AwayTeamId) ?? teams.ElementAtOrDefault(1);

        var rows = new List<MatchStatRowDto>();

        void AddRow(string label, string apiType, string suffix = "", int decimals = 0)
        {
            var h = ExtractStatValue(home, apiType);
            var a = ExtractStatValue(away, apiType);
            if (h is null && a is null)
            {
                return;
            }

            rows.Add(new MatchStatRowDto(label, FormatStatValue(h, suffix, decimals), FormatStatValue(a, suffix, decimals), h ?? 0, a ?? 0));
        }

        AddRow("Expected Goals (xG)", "expected_goals", decimals: 2);
        AddRow("Ball Possession", "Ball Possession", suffix: "%");
        AddRow("Total Shots", "Total Shots");
        AddRow("Shots on Target", "Shots on Goal");
        AddRow("Corner Kicks", "Corner Kicks");
        AddRow("Fouls", "Fouls");
        AddRow("Offsides", "Offsides");

        // Passes doesn't fit AddRow's single-stat-type shape - it combines two source fields
        // (Total passes/Passes accurate) into one "154 (73%)"-style display.
        var homePasses = ExtractStatValue(home, "Total passes");
        var awayPasses = ExtractStatValue(away, "Total passes");
        if (homePasses is not null || awayPasses is not null)
        {
            var homeAccurate = ExtractStatValue(home, "Passes accurate");
            var awayAccurate = ExtractStatValue(away, "Passes accurate");
            rows.Add(new MatchStatRowDto(
                "Passes",
                FormatPasses(homePasses, homeAccurate),
                FormatPasses(awayPasses, awayAccurate),
                homePasses ?? 0,
                awayPasses ?? 0));
        }

        AddRow("Yellow Cards", "Yellow Cards");
        AddRow("Red Cards", "Red Cards");

        return Ok(new MatchStatsResponse(rows.Count > 0, rows));
    }

    // Value comes back from API-Football as a plain number, a percentage string ("38%"), or a
    // decimal string (expected_goals, e.g. "1.83") depending on stat type - this handles all three
    // rather than assuming one shape per type, since nothing in this codebase had parsed the
    // string cases before this endpoint (only plain-int types were read previously, for corners/
    // cards settlement - see ApiFootballSettlementSupportService.ExtractStat).
    private static double? ExtractStatValue(FixtureStatisticsTeam? team, string type)
    {
        var entry = team?.Statistics.FirstOrDefault(s => s.Type == type);
        if (entry is null)
        {
            return null;
        }

        return entry.Value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => entry.Value.GetDouble(),
            System.Text.Json.JsonValueKind.String when double.TryParse(
                entry.Value.GetString()?.TrimEnd('%'), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string FormatStatValue(double? value, string suffix, int decimals) =>
        value is null ? "-" : $"{value.Value.ToString($"F{decimals}")}{suffix}";

    private static string FormatPasses(double? total, double? accurate) =>
        total is null
            ? "-"
            : accurate is null || total == 0
                // "P0" inserts a space before "%" under invariant culture ("73 %") - multiplying and
                // formatting as a plain number keeps this consistent with FormatStatValue's "73%".
                ? $"{total:F0}"
                : $"{total:F0} ({accurate / total * 100:F0}%)";

    // Match Summary's Player Stats tab. Same ExternalId-is-the-fixture-ID assumption as
    // GetMatchStats. Only players who actually appeared (Games.Minutes > 0) are returned - a full
    // squad list includes unused substitutes with every stat at zero/null, which is just noise here.
    [HttpGet("{id:guid}/player-stats")]
    public async Task<ActionResult<MatchPlayerStatsResponse>> GetPlayerStats(Guid id, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([id], ct);
        if (match is null || !long.TryParse(match.ExternalId, out var fixtureId))
        {
            return Ok(new MatchPlayerStatsResponse(false, [], []));
        }

        var teams = await playerStatsService.GetPlayerStatsAsync(fixtureId, ct);
        var home = teams.FirstOrDefault(t => t.Team.Id == match.HomeTeamId) ?? teams.ElementAtOrDefault(0);
        var away = teams.FirstOrDefault(t => t.Team.Id == match.AwayTeamId) ?? teams.ElementAtOrDefault(1);

        var homeRows = MapPlayerRows(home);
        var awayRows = MapPlayerRows(away);
        return Ok(new MatchPlayerStatsResponse(homeRows.Count > 0 || awayRows.Count > 0, homeRows, awayRows));
    }

    private static List<PlayerStatRowDto> MapPlayerRows(FixturePlayersResponseTeam? team) =>
        team?.Players
            .Select(p => (p.Player, Stats: p.Statistics.FirstOrDefault()))
            .Where(p => p.Stats?.Games?.Minutes > 0)
            .Select(p => new PlayerStatRowDto(
                p.Player.Id,
                p.Player.Name,
                p.Player.Photo,
                p.Stats!.Games!.Position,
                p.Stats.Games.Minutes ?? 0,
                p.Stats.Games.Substitute,
                p.Stats.Games.Rating,
                p.Stats.Goals?.Total ?? 0,
                p.Stats.Goals?.Assists ?? 0,
                p.Stats.Shots?.On ?? 0,
                p.Stats.Shots?.Total ?? 0,
                p.Stats.Passes?.Total ?? 0,
                PassAccuracyPercent(p.Stats.Passes),
                p.Stats.Cards?.Yellow ?? 0,
                p.Stats.Cards?.Red ?? 0))
            .ToList() ?? [];

    // Despite its name, API-Football's per-player "passes.accuracy" is a raw COUNT of completed
    // passes, not a percentage - confirmed live 2026-09-13 (e.g. a player with 68 total passes and
    // "accuracy":"59" is an 87% completion rate, not a 59% one; every sampled player had accuracy
    // <= total, which a genuine percentage field would eventually violate for a low-volume passer).
    // This computes a real percentage from the two counts, the same way FormatPasses does for the
    // team-level Stats tab (which sources from a differently-shaped, already-count-based pair of
    // fields - "Total passes"/"Passes accurate" - so isn't affected by this same mix-up).
    private static int? PassAccuracyPercent(PassesStat? passes) =>
        passes is { Total: > 0 } && int.TryParse(passes.Accuracy, out var accurate)
            ? (int)Math.Round(100.0 * accurate / passes.Total.Value)
            : null;

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
