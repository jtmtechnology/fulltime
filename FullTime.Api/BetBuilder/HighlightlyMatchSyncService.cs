using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// The only caller of RefreshLiveAsync/RefreshFixturesAsync is HighlightlyMatchSyncBackgroundService's
// timer — same choke-point pattern as BetBuilderSyncService for the extra markets. This is now the
// primary sync: fixtures, live scores, and match status all come from one
// football/matches?leagueId=X&date=Y call per HighlightlyLeagueMap.TrackedLeagueIds entry, per date
// — NOT the global (no leagueId) matches-by-date endpoint, which returns every match worldwide and
// has to be paginated through in full just to find the ones we track (confirmed 800+ matches / ~9
// pages on a busy Saturday). Costs a fixed ~14 calls per tick regardless of how many matches are
// happening worldwide that day, instead of a variable, sometimes much larger, page count.
//
// Split into two cadences to stay well within the RapidAPI daily quota: fixtures and prices barely
// change once set, so there's no need to re-poll them often, but a live match's score does — see
// HighlightlyOptions.FixtureDiscoveryIntervalMinutes.
public class HighlightlyMatchSyncService(
    HighlightlyClient client,
    AppDbContext db,
    IOptions<HighlightlyOptions> options,
    IHubContext<MatchUpdatesHub> hub,
    ILogger<HighlightlyMatchSyncService> logger)
{
    // Today's date plus the kickoff date of any match that has already KICKED OFF but never reached
    // Finished (e.g. the day rolled over before the provider reported a final score) — that's the
    // only case that would otherwise never get re-synced and never settle. Deliberately excludes
    // future Upcoming fixtures, which are also "not Finished" but don't need 30s-frequency polling
    // (RefreshFixturesAsync's daily discovery covers those) — including them blew up datesToSync to
    // every date with any fixture in the whole MatchSyncDaysAhead window (confirmed in production:
    // 20 distinct dates), multiplying every live tick's cost by ~20x for zero benefit. Also
    // deliberately excludes Postponed (previously fell under "!= Finished" too, which meant a
    // postponed match's kickoff date got re-added to every single tick forever, permanently
    // doubling this league's call count - confirmed a real contributor to the 2026-09-09 quota
    // exhaustion) - a postponed fixture is a dead end for this loop; if Highlightly ever reissues it
    // with a new date, RefreshFixturesAsync's daily forward-looking scan picks that up on its own.
    public async Task RefreshLiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var datesToSync = new HashSet<DateOnly> { DateOnly.FromDateTime(now) };

        var staleKickoffs = await db.Matches
            .Where(m => (m.Status == MatchStatus.Upcoming || m.Status == MatchStatus.InProgress) && m.KickoffTime <= now)
            .Select(m => m.KickoffTime)
            .ToListAsync(ct);

        foreach (var kickoff in staleKickoffs)
        {
            datesToSync.Add(DateOnly.FromDateTime(kickoff));
        }

        var upsertedCount = 0;
        foreach (var date in datesToSync)
        {
            upsertedCount += await FetchAndUpsertDateAsync(date, ct);
        }

        if (upsertedCount > 0)
        {
            logger.LogInformation("Live match sync: upserted {Count} tracked match(es)", upsertedCount);
        }
    }

    // The full today..today+MatchSyncDaysAhead-1 window — i.e. fixture discovery for matches that
    // aren't happening yet. Run far less often than RefreshLiveAsync (see
    // HighlightlyOptions.FixtureDiscoveryIntervalMinutes) since a fixture list doesn't need
    // minute-to-minute freshness.
    public async Task RefreshFixturesAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var upsertedCount = 0;
        foreach (var i in Enumerable.Range(0, opts.MatchSyncDaysAhead))
        {
            upsertedCount += await FetchAndUpsertDateAsync(today.AddDays(i), ct);
        }

        if (upsertedCount > 0)
        {
            logger.LogInformation("Fixture discovery: upserted {Count} tracked match(es)", upsertedCount);
        }
    }

    public Task<bool> HasLiveMatchAsync(CancellationToken ct = default) =>
        db.Matches.AnyAsync(m => m.Status == MatchStatus.InProgress, ct);

    // Backing off to the hour-long idle cadence is only safe while nothing is about to kick off —
    // otherwise a match going Upcoming -> live partway through that hour sits undetected until the
    // next tick (confirmed happening: 9 matches sat stuck at Upcoming for ~25 minutes after their
    // real kickoff). If the soonest still-Upcoming kickoff falls within the idle window, shorten the
    // wait to just past that kickoff instead, so the next tick catches it going live.
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

        // A small buffer past the kickoff itself, since a provider needs a moment to flip a match to
        // live once it actually starts. Never shorter than the live cadence (a kickoff already in
        // the past clamps to that instead of hammering the provider with a near-zero delay).
        var untilKickoff = nextKickoff - now + TimeSpan.FromSeconds(60);
        var delay = untilKickoff < TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds)
            ? TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds)
            : untilKickoff;

        return delay < idle ? delay : idle;
    }

    private async Task<int> FetchAndUpsertDateAsync(DateOnly date, CancellationToken ct)
    {
        var count = 0;

        foreach (var leagueId in HighlightlyLeagueMap.TrackedLeagueIds)
        {
            List<MatchDto> matches;
            try
            {
                matches = await client.GetMatchesAsync((int)leagueId, SeasonFor(date), date, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch matches for league {LeagueId} on {Date}", leagueId, date);
                continue;
            }

            foreach (var dto in matches.Where(IsEligibleFaCupRound))
            {
                await UpsertMatchAsync(dto, ct);
                count++;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return count;
    }

    // European domestic/UEFA seasons run August–May and are numbered by their starting year.
    private static int SeasonFor(DateOnly date) => date.Month >= 7 ? date.Year : date.Year - 1;

    private async Task UpsertMatchAsync(MatchDto dto, CancellationToken ct)
    {
        var externalId = dto.Id.ToString();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct);
        if (match is null)
        {
            match = new Match
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                HomeTeam = dto.HomeTeam.Name,
                AwayTeam = dto.AwayTeam.Name,
                KickoffTime = dto.Date,
                Status = MatchStatus.Upcoming,
            };
            db.Matches.Add(match);
        }

        match.LeagueId = dto.League.Id;
        match.Round = dto.Round;
        match.HomeTeam = dto.HomeTeam.Name;
        match.AwayTeam = dto.AwayTeam.Name;
        match.HomeTeamId = dto.HomeTeam.Id;
        match.AwayTeamId = dto.AwayTeam.Id;
        match.HomeTeamLogoUrl = dto.HomeTeam.Logo;
        match.AwayTeamLogoUrl = dto.AwayTeam.Logo;
        match.KickoffTime = dto.Date;

        var (homeScore, awayScore) = ParseScore(dto.State.Score?.Current);
        var newStatus = DeriveStatus(dto.State.Description, match.Status, match.ExternalId);
        // Confirmed via a real live match: the in-play description is "First half" (and presumably
        // "Second half"), which a bare "half" substring check wrongly flags as HT too — every
        // currently-playing match was showing as half-time. Match on "half time" specifically.
        var isHalfTime = dto.State.Description.Contains("half time", StringComparison.OrdinalIgnoreCase);

        var changed = match.HomeScore != homeScore || match.AwayScore != awayScore || match.Status != newStatus
            || match.Minute != dto.State.Clock || match.IsHalfTime != isHalfTime;

        match.HomeScore = homeScore;
        match.AwayScore = awayScore;
        match.Status = newStatus;
        match.Minute = dto.State.Clock;
        match.IsHalfTime = isHalfTime;

        if (changed)
        {
            // Fire-and-forget from the caller's perspective isn't appropriate here (a dropped
            // exception would look like a silent no-op), but a connected client missing one push
            // is harmless — it'll see the change on its next poll regardless — so this doesn't need
            // to block the sync loop or retry.
            await hub.Clients.All.SendAsync(
                "MatchUpdated",
                new MatchLiveUpdate(match.Id, homeScore, awayScore, newStatus.ToString(), dto.State.Clock, isHalfTime),
                ct);
        }
    }

    // "current" is a "H - A" string (e.g. "1 - 2"), null before kickoff.
    private static (int? Home, int? Away) ParseScore(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return (null, null);
        }

        var parts = current.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var home) || !int.TryParse(parts[1], out var away))
        {
            return (null, null);
        }

        return (home, away);
    }

    // Confirmed status strings from real data: "Not started", "Finished", "Finished after extra
    // time", "Finished after penalties", "Postponed", "First half", "Second half", and anything
    // containing "half time". Root cause of the 2026-09-06 FA Cup quota-exhaustion incident: this
    // used to default any unrecognized string (including one from that batch never identified) to
    // InProgress, which pinned the live-poll loop at its 15-30s cadence for 11+ hours overnight.
    // Now only the confirmed live-state strings map to InProgress explicitly; anything still
    // unrecognized logs a warning and keeps whatever status the match already had, so an unknown
    // string can never itself force (or keep) a match falsely "live". "Not started" is the only
    // description mapped to Upcoming - Postponed gets its own status (see MatchStatus.Postponed),
    // rather than being folded into Upcoming, precisely so RefreshLiveAsync's staleKickoffs query
    // (below) can stop re-fetching it once it's identified as postponed instead of forever treating
    // it as "hasn't kicked off yet".
    private MatchStatus DeriveStatus(string description, MatchStatus previousStatus, string externalId)
    {
        if (description == "Not started")
        {
            return MatchStatus.Upcoming;
        }

        if (description == "Postponed")
        {
            return MatchStatus.Postponed;
        }

        if (description.StartsWith("Finished", StringComparison.Ordinal))
        {
            return MatchStatus.Finished;
        }

        if (description is "First half" or "Second half"
            || description.Contains("half time", StringComparison.OrdinalIgnoreCase))
        {
            return MatchStatus.InProgress;
        }

        logger.LogWarning(
            "Unrecognized Highlightly match status {Description} for match {ExternalId}, keeping previous status {PreviousStatus}",
            description, externalId, previousStatus);
        return previousStatus;
    }

    // FA Cup's qualifying rounds and 1st/2nd Round Proper are entirely non-league/lower-league
    // clubs (this is what the 110+-simultaneous-match preliminary-round batch that originally
    // pinned DeriveStatus at InProgress for 11+ hours was) - requested to only track FA Cup from
    // the 3rd Round Proper onwards, which is also when Championship/Premier League clubs actually
    // enter. Every other tracked league is unaffected. Confirmed real 2026-09-06: Highlightly
    // returned 4 real "1st Round Qualifying" fixtures for today, all correctly excluded by this.
    private static bool IsEligibleFaCupRound(MatchDto dto)
    {
        if (dto.League.Id != HighlightlyLeagueMap.FaCup)
        {
            return true;
        }

        var round = dto.Round;
        if (string.IsNullOrEmpty(round))
        {
            return true;
        }

        if (round.Contains("Qualifying", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !round.StartsWith("1st Round", StringComparison.OrdinalIgnoreCase)
            && !round.StartsWith("2nd Round", StringComparison.OrdinalIgnoreCase);
    }
}
