using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.BetBuilder.OddsApi;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.BetBuilder.ApiFootball;

// API-Football equivalent of BetBuilderSyncService's ResolveFirstGoalScorersAsync, plus the new
// per-player stats resolution this cutover unblocks (see MatchPlayerStat, Match.TotalCorners).
// Runs on its own short cadence (ApiFootballOptions.GoalScorerResolutionIntervalMinutes) via
// ApiFootballSettlementSupportBackgroundService — a settlement-latency concern, not a
// price-freshness one, same reasoning as the Highlightly original.
public class ApiFootballSettlementSupportService(
    ApiFootballClient client,
    AppDbContext db,
    ILogger<ApiFootballSettlementSupportService> logger)
{
    // Resolves MarketType.FirstTeamToScore, which can't be derived from the final score alone.
    // A 0-0 result needs no external call (nobody scored, so "None" is certain); everything else
    // needs one fixtures/events lookup. Cutoff mirrors the Highlightly original: some fixtures
    // (lower-league/qualifying) never get an events backfill at all, so this ages out after 3 days
    // rather than re-querying forever for no benefit.
    public async Task ResolveFirstGoalScorersAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var candidates = await db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.FirstGoalScorerSide == null && m.KickoffTime >= cutoff)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return;
        }

        var resolvedCount = 0;

        foreach (var match in candidates)
        {
            if (match.HomeScore == 0 && match.AwayScore == 0)
            {
                match.FirstGoalScorerSide = SelectionSide.None;
                resolvedCount++;
                continue;
            }

            var events = await client.GetFixtureEventsAsync(long.Parse(match.ExternalId), ct);
            var firstGoal = events
                .Where(e => e.Type == "Goal")
                .OrderBy(e => e.Time.Elapsed)
                .ThenBy(e => e.Time.Extra ?? 0)
                .FirstOrDefault();

            if (firstGoal is null)
            {
                continue;
            }

            match.FirstGoalScorerSide = firstGoal.Team.Id == match.HomeTeamId
                ? SelectionSide.Home
                : firstGoal.Team.Id == match.AwayTeamId
                    ? SelectionSide.Away
                    : null;

            if (match.FirstGoalScorerSide is not null)
            {
                resolvedCount++;
            }
        }

        if (resolvedCount == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved first goalscorer for {Count} match(es)", resolvedCount);
    }

    // Powers settlement for the four player-prop market types plus TotalCorners. Same 3-day cutoff
    // reasoning as ResolveFirstGoalScorersAsync — PlayerStatsResolvedAt is set even on a fetch that
    // comes back empty, so a fixture whose stats never backfill stops being re-queried once it ages
    // out rather than piling up daily.
    public async Task ResolvePlayerStatsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var candidates = await db.Matches
            .Where(m => m.Status == MatchStatus.Finished && m.PlayerStatsResolvedAt == null && m.KickoffTime >= cutoff)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return;
        }

        var resolvedCount = 0;

        foreach (var match in candidates)
        {
            // Never trust match.ExternalId directly as an API-Football fixture ID - while
            // Highlightly is the live-score provider it's Highlightly's own match ID, a real ID
            // collision risk (both are just numeric IDs), not merely a missing lookup. Resolving by
            // team name + kickoff date instead works correctly either way (confirmed 2026-09-07,
            // same fix already applied to squad lookups - see ApiFootballEplTeamMap), at the cost of
            // one extra /fixtures call per finished match, negligible next to API-Football's PRO
            // tier 7,500/day quota.
            var fixture = await ResolveFixtureAsync(match, ct);
            if (fixture is null)
            {
                logger.LogWarning("Could not resolve an API-Football fixture for match {MatchId}", match.Id);
                continue;
            }

            var fixtureId = fixture.Fixture.Id;

            List<Dtos.FixturePlayersResponseTeam> teams;
            List<Dtos.FixtureStatisticsTeam> statistics;
            try
            {
                teams = await client.GetFixturePlayerStatsAsync(fixtureId, ct);
                statistics = await client.GetFixtureStatisticsAsync(fixtureId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch player stats for match {MatchId} (fixture {FixtureId})", match.Id, fixtureId);
                continue;
            }

            await db.MatchPlayerStats.Where(s => s.MatchId == match.Id).ExecuteDeleteAsync(ct);

            foreach (var team in teams)
            {
                // Compared against the resolved fixture's own team IDs (API-Football's), not
                // match.HomeTeamId/AwayTeamId (Highlightly's) - same reasoning as the fixture
                // resolution above.
                var side = team.Team.Id == fixture.Teams.Home.Id ? SelectionSide.Home
                    : team.Team.Id == fixture.Teams.Away.Id ? SelectionSide.Away
                    : (SelectionSide?)null;
                if (side is null)
                {
                    continue;
                }

                foreach (var entry in team.Players)
                {
                    var stat = entry.Statistics.FirstOrDefault();
                    if (stat is null)
                    {
                        continue;
                    }

                    db.MatchPlayerStats.Add(new MatchPlayerStat
                    {
                        Id = Guid.NewGuid(),
                        MatchId = match.Id,
                        PlayerName = entry.Player.Name,
                        Team = side.Value,
                        Goals = stat.Goals?.Total ?? 0,
                        Assists = stat.Goals?.Assists ?? 0,
                        ShotsOnTarget = stat.Shots?.On ?? 0,
                        YellowCards = stat.Cards?.Yellow ?? 0,
                    });
                }
            }

            match.TotalCorners = SumCornerKicks(statistics);
            match.PlayerStatsResolvedAt = DateTime.UtcNow;
            resolvedCount++;
        }

        if (resolvedCount == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Resolved player stats for {Count} match(es)", resolvedCount);
    }

    // Looks up the real API-Football fixture for a match by league + kickoff date, then picks the
    // best team-name match among that day's fixtures (there's rarely more than one match between
    // the same two competitions on the same day, so this is unambiguous in practice). Falls back to
    // treating match.LeagueId as an API-Football league ID directly when it's not in the bridge map
    // - covers the dormant LiveScoreSource=ApiFootball cutover, where it already is one.
    private async Task<FixtureDto?> ResolveFixtureAsync(Match match, CancellationToken ct)
    {
        var apiFootballLeagueId = HighlightlyToApiFootballLeagueMap.LeagueIds.GetValueOrDefault(match.LeagueId, match.LeagueId);
        var date = DateOnly.FromDateTime(match.KickoffTime);

        List<FixtureDto> fixtures;
        try
        {
            fixtures = await client.GetFixturesAsync((int)apiFootballLeagueId, SeasonFor(date), date, date, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to look up API-Football fixtures for match {MatchId}", match.Id);
            return null;
        }

        var candidate = new TeamNameMatcher.MatchCandidate(match.HomeTeam, match.AwayTeam, match.KickoffTime);
        var pairs = fixtures.Select(f => (Fixture: f, Candidate: new TeamNameMatcher.MatchCandidate(
            f.Teams.Home.Name, f.Teams.Away.Name, f.Fixture.Date.UtcDateTime)));
        return TeamNameMatcher.FindBest(pairs, candidate)?.OddsEvent;
    }

    // Same convention as ApiFootballMatchSyncService.SeasonFor - API-Football's "season" is the
    // year the season started in (a March fixture in year Y is season Y-1).
    private static int SeasonFor(DateOnly date) => date.Month >= 7 ? date.Year : date.Year - 1;

    private static int? SumCornerKicks(List<Dtos.FixtureStatisticsTeam> statistics)
    {
        var total = 0;
        var found = false;

        foreach (var team in statistics)
        {
            var corners = team.Statistics.FirstOrDefault(s => s.Type == "Corner Kicks");
            if (corners is null || corners.Value.ValueKind != System.Text.Json.JsonValueKind.Number)
            {
                continue;
            }

            total += corners.Value.GetInt32();
            found = true;
        }

        return found ? total : null;
    }
}
