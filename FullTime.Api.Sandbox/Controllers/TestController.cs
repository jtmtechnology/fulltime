using FullTime.Api.Sandbox.Data;
using FullTime.Api.Sandbox.Models;
using FullTime.Api.Sandbox.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.Sandbox.Controllers;

// Manual, on-demand endpoints for evaluating API-Football and the-odds-api against real data -
// deliberately no background timers here (unlike Highlightly's *BackgroundService pattern in
// FullTime.Api). Every call is triggered explicitly via curl so quota usage stays fully under
// control while testing, and every response carries back the provider's own rate-limit headers
// (see RateLimitInfo) so cost is visible without tailing VM logs.
[ApiController]
[Route("test")]
public class TestController(
    ApiFootballClient apiFootball,
    OddsApiClient oddsApi,
    SandboxDbContext db) : ControllerBase
{
    // GET /test/live-sync?leagueIds=39,40 - omit leagueIds to upsert every live match worldwide.
    [HttpGet("live-sync")]
    public async Task<IActionResult> LiveSync([FromQuery] string? leagueIds, CancellationToken ct)
    {
        var filter = leagueIds?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(long.Parse).ToHashSet();

        var result = await apiFootball.GetLiveFixturesAsync(ct);
        var fixtures = filter is null
            ? result.Data
            : result.Data.Where(f => filter.Contains(f.League.Id)).ToList();

        var upserted = 0;
        foreach (var f in fixtures)
        {
            await UpsertAsync(f, ct);
            upserted++;
        }

        if (upserted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            totalLiveWorldwide = result.Data.Count,
            upserted,
            rateLimit = result.RateLimit.Headers,
            sample = fixtures.Take(5).Select(f => new
            {
                f.League.Name,
                Home = f.Teams.Home.Name,
                Away = f.Teams.Away.Name,
                Status = f.Fixture.Status.Short,
                f.Fixture.Status.Elapsed,
            }),
        });
    }

    // GET /test/fixture-discovery?leagueId=39&season=2026&days=7
    [HttpGet("fixture-discovery")]
    public async Task<IActionResult> FixtureDiscovery(
        [FromQuery] int leagueId, [FromQuery] int season, [FromQuery] int days, CancellationToken ct)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = from.AddDays(days);

        var result = await apiFootball.GetFixturesAsync(leagueId, season, from, to, ct);

        foreach (var f in result.Data)
        {
            await UpsertAsync(f, ct);
        }

        if (result.Data.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            upserted = result.Data.Count,
            rateLimit = result.RateLimit.Headers,
        });
    }

    // GET /test/matches - inspect what's landed in the sandbox DB so far.
    [HttpGet("matches")]
    public async Task<IActionResult> Matches(CancellationToken ct) =>
        Ok(await db.Matches.OrderBy(m => m.KickoffTime).ToListAsync(ct));

    // GET /test/odds-sports - free call, lists every sport key the-odds-api knows about.
    [HttpGet("odds-sports")]
    public async Task<IActionResult> OddsSports(CancellationToken ct)
    {
        var result = await oddsApi.GetSportsAsync(ct);
        return Ok(new { count = result.Data.Count, rateLimit = result.RateLimit.Headers, sports = result.Data });
    }

    // GET /test/odds-events?sport=soccer_epl - free call, lists upcoming/live event IDs.
    [HttpGet("odds-events")]
    public async Task<IActionResult> OddsEvents([FromQuery] string sport, CancellationToken ct)
    {
        var result = await oddsApi.GetEventsAsync(sport, ct);
        return Ok(new { rateLimit = result.RateLimit.Headers, events = result.Data });
    }

    // GET /test/odds?sport=soccer_epl&eventId=X&markets=h2h,btts&regions=uk - costs markets x
    // regions requests, see OddsApiClient.
    [HttpGet("odds")]
    public async Task<IActionResult> Odds(
        [FromQuery] string sport, [FromQuery] string eventId, [FromQuery] string markets,
        [FromQuery] string regions, CancellationToken ct)
    {
        var result = await oddsApi.GetEventOddsAsync(sport, eventId, markets, regions, ct);
        var foundMarketKeys = result.Data.Bookmakers
            .SelectMany(b => b.Markets)
            .Select(m => m.Key)
            .Distinct()
            .ToList();

        return Ok(new
        {
            rateLimit = result.RateLimit.Headers,
            requestedMarkets = markets.Split(','),
            marketsWithData = foundMarketKeys,
            raw = result.Data,
        });
    }

    // GET /test/fuzzy-match?sport=soccer_epl&leagueId=39&season=2026&days=7 - pairs the-odds-api's
    // events (plain team-name strings, no shared ID with anything) against our own SandboxMatch
    // rows sourced from API-Football, via TeamNameMatcher. Read-only diagnostic, no DB writes - the
    // whole point is seeing every pairing and its score before trusting this for real linking.
    // Needs fixture-discovery to have populated SandboxMatch for this league first (run
    // /test/fixture-discovery), which needs the PRO tier - API-Football's free plan blocks
    // current-season fixture queries entirely (confirmed 2026-09-06).
    [HttpGet("fuzzy-match")]
    public async Task<IActionResult> FuzzyMatch(
        [FromQuery] string sport, [FromQuery] long leagueId, CancellationToken ct)
    {
        var oddsResult = await oddsApi.GetEventsAsync(sport, ct);
        var ourMatches = await db.Matches.Where(m => m.LeagueId == leagueId).ToListAsync(ct);

        var oddsCandidates = oddsResult.Data
            .Select(e => new TeamNameMatcher.MatchCandidate(e.HomeTeam, e.AwayTeam, e.CommenceTime))
            .ToList();
        var ourCandidates = ourMatches
            .Select(m => new TeamNameMatcher.MatchCandidate(m.HomeTeam, m.AwayTeam, m.KickoffTime))
            .ToList();

        var pairings = TeamNameMatcher.MatchAll(oddsCandidates, ourCandidates);

        return Ok(new
        {
            rateLimit = oddsResult.RateLimit.Headers,
            oddsApiEventCount = oddsCandidates.Count,
            ourMatchCount = ourCandidates.Count,
            pairings = pairings.Select(p => new
            {
                oddsApi = $"{p.OddsEvent.HomeTeam} vs {p.OddsEvent.AwayTeam}",
                oddsApiKickoff = p.OddsEvent.KickoffTime,
                matched = p.Match is null ? null : $"{p.Match.HomeTeam} vs {p.Match.AwayTeam}",
                matchedKickoff = p.Match?.KickoffTime,
                score = Math.Round(p.Score, 3),
            }),
        });
    }

    // GET /test/player-props?matchId=X&sport=soccer_epl&eventId=Y&markets=player_goal_scorer_anytime,player_assists
    // Stores every returned outcome against one of our SandboxMatch rows - matchId is supplied
    // directly by the caller (e.g. from a /test/fuzzy-match result you've eyeballed and trust),
    // deliberately not auto-linked, so a bad fuzzy match can't silently attach props to the wrong
    // fixture while testing.
    [HttpPost("player-props")]
    public async Task<IActionResult> PlayerProps(
        [FromQuery] Guid matchId, [FromQuery] string sport, [FromQuery] string eventId,
        [FromQuery] string markets, [FromQuery] string regions, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([matchId], ct);
        if (match is null)
        {
            return NotFound($"No sandbox match with id {matchId} - run /test/live-sync or /test/fixture-discovery first.");
        }

        var result = await oddsApi.GetEventOddsAsync(sport, eventId, markets, regions, ct);

        // Full replace, not an upsert - this endpoint has no natural row key to upsert against
        // (the-odds-api doesn't give one), and repeated test calls were silently piling up
        // duplicate rows every time, some tagged with whatever Team logic existed when they were
        // first inserted. AppCompatController's "oldest row wins" grouping was then serving those
        // stale duplicates back to the app instead of the latest fetch.
        await db.PlayerPropMarkets.Where(p => p.MatchId == matchId).ExecuteDeleteAsync(ct);

        // Squad lookups to resolve which team each player belongs to - the-odds-api has no
        // team/roster data of its own (see SandboxPlayerPropMarket.Team's comment on the surname-
        // matching this needs, since API-Football abbreviates first names and the-odds-api doesn't).
        var homeSquad = await apiFootball.GetSquadPlayerNamesAsync(match.HomeTeamId, ct);
        var awaySquad = await apiFootball.GetSquadPlayerNamesAsync(match.AwayTeamId, ct);
        var homeSurnames = homeSquad.Data.Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var awaySurnames = awaySquad.Data.Select(Surname).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stored = 0;
        foreach (var bookmaker in result.Data.Bookmakers)
        {
            foreach (var market in bookmaker.Markets)
            {
                foreach (var outcome in market.Outcomes)
                {
                    // Player-prop outcomes carry the player's name in "description" ("Yes"/"No" is
                    // the outer "name") - confirmed 2026-09-06. Fall back to Name for markets like
                    // h2h that have no per-player description, so this stays reusable for those too.
                    var playerName = outcome.Description ?? outcome.Name;
                    var surname = Surname(playerName);
                    var team = homeSurnames.Contains(surname) ? "Home"
                        : awaySurnames.Contains(surname) ? "Away"
                        : null;

                    db.PlayerPropMarkets.Add(new SandboxPlayerPropMarket
                    {
                        Id = Guid.NewGuid(),
                        MatchId = matchId,
                        MarketKey = market.Key,
                        PlayerName = playerName,
                        Team = team,
                        Side = outcome.Name,
                        Bookmaker = bookmaker.Title,
                        Price = outcome.Price,
                        Point = outcome.Point,
                        FetchedAt = DateTime.UtcNow,
                    });
                    stored++;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            stored,
            unmatchedTeam = await db.PlayerPropMarkets.Where(p => p.MatchId == matchId && p.Team == null)
                .Select(p => p.PlayerName).Distinct().ToListAsync(ct),
            rateLimit = result.RateLimit.Headers,
        });
    }

    // GET /test/match-markets?matchId=X&sport=soccer_epl&eventId=Y&markets=h2h,totals,btts,alternate_totals_corners
    // Match-level markets (not per-player) - h2h/totals/btts feed the Popular tab's standard
    // markets, alternate_totals_corners feeds its new corners section. Team resolution here is
    // exact-string against HomeTeam/AwayTeam (h2h's own outcome names are the literal team names,
    // e.g. "Everton"/"Manchester United"/"Draw") - no squad/surname lookup needed, unlike player
    // props. Confirmed 2026-09-06: alternate_team_totals_corners (per-team corners) isn't actually
    // priced by any bookmaker for a real EPL match tried, so it's not wired up here.
    [HttpPost("match-markets")]
    public async Task<IActionResult> MatchMarkets(
        [FromQuery] Guid matchId, [FromQuery] string sport, [FromQuery] string eventId,
        [FromQuery] string markets, [FromQuery] string regions, CancellationToken ct)
    {
        var match = await db.Matches.FindAsync([matchId], ct);
        if (match is null)
        {
            return NotFound($"No sandbox match with id {matchId} - run /test/live-sync or /test/fixture-discovery first.");
        }

        var result = await oddsApi.GetEventOddsAsync(sport, eventId, markets, regions, ct);
        var requestedKeys = markets.Split(',');
        await db.PlayerPropMarkets.Where(p => p.MatchId == matchId && requestedKeys.Contains(p.MarketKey)).ExecuteDeleteAsync(ct);

        var stored = 0;
        foreach (var bookmaker in result.Data.Bookmakers)
        {
            foreach (var market in bookmaker.Markets)
            {
                foreach (var outcome in market.Outcomes)
                {
                    // correct_score packs both scores into one outcome name -
                    // "Everton:1|Manchester United:0" - skip anything that doesn't parse against
                    // this match's own team names rather than store garbage.
                    if (market.Key == "correct_score")
                    {
                        var parsed = ParseCorrectScore(outcome.Name, match.HomeTeam, match.AwayTeam);
                        if (parsed is null)
                        {
                            continue;
                        }

                        db.PlayerPropMarkets.Add(new SandboxPlayerPropMarket
                        {
                            Id = Guid.NewGuid(),
                            MatchId = matchId,
                            MarketKey = market.Key,
                            PlayerName = null,
                            PredictedHomeScore = parsed.Value.Home,
                            PredictedAwayScore = parsed.Value.Away,
                            Bookmaker = bookmaker.Title,
                            Price = outcome.Price,
                            FetchedAt = DateTime.UtcNow,
                        });
                        stored++;
                        continue;
                    }

                    var team = outcome.Name == match.HomeTeam ? "Home"
                        : outcome.Name == match.AwayTeam ? "Away"
                        : null;

                    db.PlayerPropMarkets.Add(new SandboxPlayerPropMarket
                    {
                        Id = Guid.NewGuid(),
                        MatchId = matchId,
                        MarketKey = market.Key,
                        PlayerName = null,
                        Team = team,
                        // h2h's outcomes are literally the team names/"Draw", not a Home/Away/Draw
                        // side label - normalize so BetBuilder.razor's switch on Side can treat h2h
                        // the same way FirstTeamToScore etc. already work.
                        Side = market.Key == "h2h"
                            ? (team ?? (outcome.Name == "Draw" ? "Draw" : outcome.Name))
                            : outcome.Name,
                        Bookmaker = bookmaker.Title,
                        Price = outcome.Price,
                        Point = outcome.Point,
                        FetchedAt = DateTime.UtcNow,
                    });
                    stored++;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return Ok(new { stored, rateLimit = result.RateLimit.Headers });
    }

    // GET /test/player-props/{matchId} - inspect what's stored for one match.
    [HttpGet("player-props/{matchId:guid}")]
    public async Task<IActionResult> GetPlayerProps(Guid matchId, CancellationToken ct) =>
        Ok(await db.PlayerPropMarkets
            .Where(p => p.MatchId == matchId)
            .OrderBy(p => p.MarketKey).ThenBy(p => p.PlayerName)
            .ToListAsync(ct));

    // "J. Pickford" -> "Pickford", "Jordan Pickford" -> "Pickford" - the common ground between
    // API-Football's abbreviated squad names and the-odds-api's full ones. Hyphenated surnames
    // ("Maitland-Niles") stay one token on both sides, so this holds up for them too.
    private static string Surname(string name) =>
        name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : name;

    // "Everton:1|Manchester United:0" -> (Home: 1, Away: 0), matched against this match's own team
    // names so it comes out right regardless of which order the-odds-api lists the two sides in.
    private static (int Home, int Away)? ParseCorrectScore(string name, string homeTeam, string awayTeam)
    {
        var parts = name.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        int? home = null, away = null;
        foreach (var part in parts)
        {
            var pieces = part.Split(':', 2);
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out var score))
            {
                continue;
            }

            if (pieces[0] == homeTeam) home = score;
            else if (pieces[0] == awayTeam) away = score;
        }

        return home is { } h && away is { } a ? (h, a) : null;
    }

    private async Task UpsertAsync(Dtos.FixtureDto f, CancellationToken ct)
    {
        var externalId = f.Fixture.Id.ToString();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct);
        if (match is null)
        {
            match = new SandboxMatch
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                LeagueName = f.League.Name,
                HomeTeam = f.Teams.Home.Name,
                AwayTeam = f.Teams.Away.Name,
                KickoffTime = f.Fixture.Date.UtcDateTime,
                StatusShort = f.Fixture.Status.Short,
            };
            db.Matches.Add(match);
        }

        match.LeagueId = f.League.Id;
        match.LeagueName = f.League.Name;
        match.HomeTeam = f.Teams.Home.Name;
        match.AwayTeam = f.Teams.Away.Name;
        match.HomeTeamId = f.Teams.Home.Id;
        match.AwayTeamId = f.Teams.Away.Id;
        match.HomeTeamLogoUrl = f.Teams.Home.Logo;
        match.AwayTeamLogoUrl = f.Teams.Away.Logo;
        match.KickoffTime = f.Fixture.Date.UtcDateTime;
        match.StatusShort = f.Fixture.Status.Short;
        match.Elapsed = f.Fixture.Status.Elapsed;
        match.HomeScore = f.Goals?.Home;
        match.AwayScore = f.Goals?.Away;
        match.LastSyncedAt = DateTime.UtcNow;
    }
}
