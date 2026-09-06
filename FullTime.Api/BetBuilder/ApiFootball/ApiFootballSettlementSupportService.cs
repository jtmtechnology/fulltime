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
            var fixtureId = long.Parse(match.ExternalId);

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
                var side = team.Team.Id == match.HomeTeamId ? SelectionSide.Home
                    : team.Team.Id == match.AwayTeamId ? SelectionSide.Away
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
