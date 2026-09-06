using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Live-score replacement for HighlightlyMatchSyncService, active when
// Providers:LiveScoreSource == "ApiFootball" (see Program.cs). The whole point of this cutover:
// RefreshLiveAsync costs ONE fixtures?live=all call per tick regardless of how many matches/leagues
// are live worldwide, instead of Highlightly's ~14-calls-per-tick (one per tracked league) model —
// see ApiFootballClient.GetLiveFixturesAsync.
public class ApiFootballMatchSyncService(
    ApiFootballClient client,
    AppDbContext db,
    IOptions<ApiFootballOptions> options,
    IHubContext<MatchUpdatesHub> hub,
    ILogger<ApiFootballMatchSyncService> logger)
{
    public async Task RefreshLiveAsync(CancellationToken ct = default)
    {
        var fixtures = await client.GetLiveFixturesAsync(ct);
        var tracked = fixtures.Where(f => ApiFootballLeagueMap.TrackedLeagueIds.Contains(f.League.Id)).ToList();

        var upsertedCount = 0;
        foreach (var fixture in tracked)
        {
            await UpsertMatchAsync(fixture, ct);
            upsertedCount++;
        }

        if (upsertedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("API-Football live sync: upserted {Count} tracked match(es)", upsertedCount);
        }
    }

    // Per-league fixture discovery — API-Football has no "all upcoming fixtures across leagues"
    // endpoint, so this still costs one call per tracked league (same shape as Highlightly's
    // RefreshFixturesAsync), just run far less often than the live loop.
    public async Task RefreshFixturesAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = today.AddDays(opts.MatchSyncDaysAhead - 1);

        var upsertedCount = 0;
        foreach (var leagueId in ApiFootballLeagueMap.TrackedLeagueIds)
        {
            List<FixtureDto> fixtures;
            try
            {
                fixtures = await client.GetFixturesAsync((int)leagueId, SeasonFor(today), today, to, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch fixtures for league {LeagueId}", leagueId);
                continue;
            }

            foreach (var fixture in fixtures)
            {
                await UpsertMatchAsync(fixture, ct);
                upsertedCount++;
            }
        }

        if (upsertedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("API-Football fixture discovery: upserted {Count} tracked match(es)", upsertedCount);
        }
    }

    public Task<bool> HasLiveMatchAsync(CancellationToken ct = default) =>
        db.Matches.AnyAsync(m => m.Status == MatchStatus.InProgress, ct);

    public async Task<TimeSpan> NextPollDelayAsync(CancellationToken ct = default)
    {
        var opts = options.Value;

        if (await HasLiveMatchAsync(ct))
        {
            return TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds);
        }

        var now = DateTime.UtcNow;
        var idle = TimeSpan.FromSeconds(opts.IdleRefreshIntervalSeconds);

        var nextKickoff = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime <= now + idle)
            .OrderBy(m => m.KickoffTime)
            .Select(m => m.KickoffTime)
            .FirstOrDefaultAsync(ct);

        if (nextKickoff == default)
        {
            return idle;
        }

        var untilKickoff = nextKickoff - now + TimeSpan.FromSeconds(60);
        var delay = untilKickoff < TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds)
            ? TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds)
            : untilKickoff;

        return delay < idle ? delay : idle;
    }

    private static int SeasonFor(DateOnly date) => date.Month >= 7 ? date.Year : date.Year - 1;

    private async Task UpsertMatchAsync(FixtureDto dto, CancellationToken ct)
    {
        var externalId = dto.Fixture.Id.ToString();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct);
        if (match is null)
        {
            match = new Match
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                HomeTeam = dto.Teams.Home.Name,
                AwayTeam = dto.Teams.Away.Name,
                KickoffTime = dto.Fixture.Date.UtcDateTime,
                Status = MatchStatus.Upcoming,
            };
            db.Matches.Add(match);
        }

        match.LeagueId = dto.League.Id;
        match.HomeTeam = dto.Teams.Home.Name;
        match.AwayTeam = dto.Teams.Away.Name;
        match.HomeTeamId = dto.Teams.Home.Id;
        match.AwayTeamId = dto.Teams.Away.Id;
        match.HomeTeamLogoUrl = dto.Teams.Home.Logo;
        match.AwayTeamLogoUrl = dto.Teams.Away.Logo;
        match.KickoffTime = dto.Fixture.Date.UtcDateTime;

        var newStatus = DeriveStatus(dto.Fixture.Status.Short, match.Status, externalId);
        var isHalfTime = dto.Fixture.Status.Short == "HT";

        var changed = match.HomeScore != dto.Goals?.Home || match.AwayScore != dto.Goals?.Away
            || match.Status != newStatus || match.Minute != dto.Fixture.Status.Elapsed || match.IsHalfTime != isHalfTime;

        match.HomeScore = dto.Goals?.Home;
        match.AwayScore = dto.Goals?.Away;
        match.Status = newStatus;
        match.Minute = dto.Fixture.Status.Elapsed;
        match.IsHalfTime = isHalfTime;

        if (changed)
        {
            await hub.Clients.All.SendAsync(
                "MatchUpdated",
                new MatchLiveUpdate(match.Id, match.HomeScore, match.AwayScore, newStatus.ToString(), match.Minute, isHalfTime),
                ct);
        }
    }

    // API-Football's short status codes. Confirmed live: NS, 1H, HT, 2H, FT. The rest (ET, P, PEN,
    // SUSP, INT, PST, CANC, ABD, AWD, WO) are API-Football's own documented set, not yet seen live —
    // mapped defensively rather than assumed, and — per the DeriveStatus incident this whole cutover
    // is partly fixing (see HighlightlyMatchSyncService.DeriveStatus) — anything still unrecognized
    // logs a warning and keeps the match's previous status rather than silently guessing InProgress.
    private MatchStatus DeriveStatus(string statusShort, MatchStatus previousStatus, string externalId)
    {
        switch (statusShort)
        {
            case "NS" or "TBD" or "PST":
                return MatchStatus.Upcoming;
            case "FT" or "AET" or "PEN" or "AWD" or "WO" or "CANC" or "ABD":
                return MatchStatus.Finished;
            case "1H" or "2H" or "HT" or "ET" or "P" or "SUSP" or "INT":
                return MatchStatus.InProgress;
        }

        logger.LogWarning(
            "Unrecognized API-Football status {StatusShort} for fixture {ExternalId}, keeping previous status {PreviousStatus}",
            statusShort, externalId, previousStatus);
        return previousStatus;
    }
}
