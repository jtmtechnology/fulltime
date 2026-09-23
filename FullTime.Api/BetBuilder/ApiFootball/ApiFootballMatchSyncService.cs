using FullTime.Api.Auth;
using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Notifications;
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
    IEmailSender emailSender,
    MatchAlertService matchAlerts,
    ILogger<ApiFootballMatchSyncService> logger)
{
    // Per-match, once per UTC date - same reasoning as HighlightlyClient's quota-alert dedup: a
    // stuck match should keep nagging daily until fixed, but not once per tick. In-memory only
    // (resets on restart), acceptable for a monitoring feature same as the quota alerts.
    private static readonly Dictionary<Guid, DateOnly> _staleAlertSentDates = [];

    public async Task RefreshLiveAsync(CancellationToken ct = default)
    {
        var fixtures = await client.GetLiveFixturesAsync(ct);
        var tracked = fixtures
            .Where(f => ApiFootballLeagueMap.TrackedLeagueIds.Contains(f.League.Id))
            .ToList();

        var upsertedCount = 0;
        foreach (var fixture in tracked)
        {
            if (await UpsertMatchAsync(fixture, ct))
            {
                upsertedCount++;
            }
        }

        // A match that's still InProgress in our DB but didn't come back in this tick's live=all
        // response almost certainly just finished and dropped off the live set - re-fetch it
        // directly rather than waiting for the once-a-day fixture-discovery tick to eventually
        // notice (confirmed in production 2026-09-10: matches sat stuck at their last-seen live
        // minute for hours after actually finishing).
        var liveExternalIds = tracked.Select(f => f.Fixture.Id.ToString()).ToHashSet();
        var droppedFromLive = await db.Matches
            .Where(m => m.Status == MatchStatus.InProgress && !liveExternalIds.Contains(m.ExternalId))
            .Select(m => m.ExternalId)
            .ToListAsync(ct);

        if (droppedFromLive.Count > 0)
        {
            // A failure here (e.g. a transient throttle) must not cost the tracked-fixtures upserts
            // above their save - same "log and move on" resilience as RefreshFixturesAsync's
            // per-league fetch, just for this narrower follow-up call.
            try
            {
                var followUp = await client.GetFixturesByIdsAsync(droppedFromLive.Select(long.Parse), ct);
                foreach (var fixture in followUp)
                {
                    if (await UpsertMatchAsync(fixture, ct))
                    {
                        upsertedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to re-fetch {Count} match(es) dropped from live=all", droppedFromLive.Count);
            }
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
                if (await UpsertMatchAsync(fixture, ct))
                {
                    upsertedCount++;
                }
            }
        }

        if (upsertedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("API-Football fixture discovery: upserted {Count} tracked match(es)", upsertedCount);
        }

        await RecheckStaleUpcomingAsync(ct);
    }

    // Closes a gap RefreshFixturesAsync's own date-windowed query can't: that query only ever asks
    // the provider for fixtures from today onwards, so once a match's original kickoff date is in
    // the past it silently drops out of every future discovery tick's window - a postponement missed
    // on the day it was announced would otherwise never be rechecked again (confirmed in production
    // 2026-09-17 - see HANDOVER.md §24). Fetching by fixture ID via GetFixturesByIdsAsync has no date
    // filter at all, so it reaches a match regardless of how long ago its date slipped into the past.
    private async Task RecheckStaleUpcomingAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-options.Value.StaleUpcomingMinutes);
        var staleExternalIds = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime <= cutoff)
            .Select(m => m.ExternalId)
            .ToListAsync(ct);

        if (staleExternalIds.Count == 0)
        {
            return;
        }

        List<FixtureDto> fixtures;
        try
        {
            fixtures = await client.GetFixturesByIdsAsync(staleExternalIds.Select(long.Parse), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to re-check {Count} stale-Upcoming match(es)", staleExternalIds.Count);
            return;
        }

        var upsertedCount = 0;
        foreach (var fixture in fixtures)
        {
            if (await UpsertMatchAsync(fixture, ct))
            {
                upsertedCount++;
            }
        }

        if (upsertedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "API-Football stale-Upcoming recheck: re-fetched {Count} match(es) still Upcoming past kickoff", upsertedCount);
        }
    }

    // Excludes matches stuck InProgress well past any real match's actual duration (extra time +
    // penalties + delays all included) - a stuck row must not be allowed to force the expensive
    // live-refresh cadence forever (see CheckStaleInProgressMatchesAsync, StaleInProgressMinutes).
    public Task<bool> HasLiveMatchAsync(CancellationToken ct = default)
    {
        var staleCutoff = DateTime.UtcNow.AddMinutes(-options.Value.StaleInProgressMinutes);
        return db.Matches.AnyAsync(m => m.Status == MatchStatus.InProgress && m.KickoffTime > staleCutoff, ct);
    }

    // A match this long past kickoff and still InProgress almost certainly never got a real final
    // whistle recorded - abandoned game, or API-Football dropped it from fixtures?live=all before
    // sending a clean "FT". HasLiveMatchAsync above already stops treating it as "live" for cadence
    // purposes, but it still needs a human to actually look at it (settlement for any bets on it is
    // stuck too, since SettlementService only acts once Status == Finished) - hence the alert.
    public async Task CheckStaleInProgressMatchesAsync(CancellationToken ct = default)
    {
        var staleCutoff = DateTime.UtcNow.AddMinutes(-options.Value.StaleInProgressMinutes);
        var staleMatches = await db.Matches
            .Where(m => m.Status == MatchStatus.InProgress && m.KickoffTime <= staleCutoff)
            .ToListAsync(ct);

        if (staleMatches.Count == 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var toAlert = staleMatches.Where(m => !_staleAlertSentDates.TryGetValue(m.Id, out var sent) || sent != today).ToList();
        if (toAlert.Count == 0)
        {
            return;
        }

        foreach (var match in toAlert)
        {
            _staleAlertSentDates[match.Id] = today;
            logger.LogWarning(
                "Match {MatchId} ({HomeTeam} v {AwayTeam}, kicked off {KickoffTime:O}) has been InProgress for over {Minutes} minutes - likely stuck",
                match.Id, match.HomeTeam, match.AwayTeam, match.KickoffTime, options.Value.StaleInProgressMinutes);
        }

        var toEmail = options.Value.AlertEmail;
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            return;
        }

        var body = "The following match(es) have been stuck at InProgress well past a normal match duration " +
            $"(over {options.Value.StaleInProgressMinutes} minutes since kickoff) and likely never received a real " +
            "final whistle from API-Football:\n\n" +
            string.Join("\n", toAlert.Select(m => $"- {m.HomeTeam} v {m.AwayTeam} (kicked off {m.KickoffTime:yyyy-MM-dd HH:mm} UTC)")) +
            "\n\nThey no longer force the fast live-refresh cadence, but bets on them can't settle until their " +
            "Status/scores are corrected - check API-Football's real status for these fixtures directly.";

        // Fire-and-forget, same reasoning as HighlightlyClient.SendAlertEmail - a failed alert must
        // never break the sync loop that triggered it.
        _ = Task.Run(async () =>
        {
            try
            {
                await emailSender.SendAsync(toEmail, "FullTime: match(es) stuck InProgress", body);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send stale-InProgress alert email to {ToEmail}", toEmail);
            }
        }, ct);
    }

    public async Task<TimeSpan> NextPollDelayAsync(CancellationToken ct = default)
    {
        var opts = options.Value;

        if (await HasLiveMatchAsync(ct))
        {
            return TimeSpan.FromSeconds(opts.LiveRefreshIntervalSeconds);
        }

        var now = DateTime.UtcNow;
        var idle = TimeSpan.FromSeconds(opts.IdleRefreshIntervalSeconds);

        // KickoffTime >= now guards against a match stuck at Upcoming past its own kickoff (e.g. a
        // postponement our sync never caught - confirmed in production 2026-09-17: Levante v Athletic
        // Club sat Upcoming with a kickoff 18+ hours in the past, always sorted first below, and its
        // permanently-negative "time until kickoff" was pinning every tick to the fast
        // LiveRefreshIntervalSeconds cadence instead of the idle one). A stuck row like that still
        // needs fixing at the data level, but must never be able to force fast-polling forever.
        var nextKickoff = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime >= now && m.KickoffTime <= now + idle)
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

    // Returns false only when the fixture was skipped with nothing written - callers use it to decide
    // whether a SaveChangesAsync is needed at all.
    private async Task<bool> UpsertMatchAsync(FixtureDto dto, CancellationToken ct)
    {
        var externalId = dto.Fixture.Id.ToString();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct);

        // Gated here on every upsert, not just at discovery: API-Football's round label for early FA
        // Cup ties isn't reliable when a fixture is first published (confirmed 2026-09-22/23 - 13
        // 2nd Round Qualifying ties got in under a label the filter let through, then had their real
        // round written over it by later syncs, and one sibling tie is still labelled
        // "Quarter-finals"). The refetch-by-ID paths (dropped-from-live, stale-Upcoming) also never
        // filtered, so once a row was in, nothing ever took it back out.
        if (!IsEligibleFaCupRound(dto))
        {
            return match is not null && await RemoveIneligibleMatchAsync(match, dto.League.Round, ct);
        }
        // A brand-new row's "previous" status/score are just freshly-initialized defaults, not a
        // real prior tick - without this, the very first time we see a match that's already
        // in-progress (e.g. a restart, or fixture discovery losing a race with the live sync - see
        // this file's own note on that race elsewhere) would read as "just kicked off" even if it's
        // actually deep into the second half. Alert-firing below is skipped entirely for this tick;
        // its real transitions get caught correctly from the next tick onwards.
        var isNewMatch = match is null;
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

        // Store Highlightly's own ID here, not API-Football's raw one - the client's
        // LeagueCatalog/MatchLeaguePreferences are keyed on Highlightly's IDs regardless of which
        // provider is actually live (see HighlightlyToApiFootballLeagueMap's reverse-map comment).
        match.LeagueId = HighlightlyToApiFootballLeagueMap.ApiFootballToHighlightlyLeagueIds
            .GetValueOrDefault(dto.League.Id, dto.League.Id);
        match.Round = dto.League.Round;
        match.HomeTeam = dto.Teams.Home.Name;
        match.AwayTeam = dto.Teams.Away.Name;
        match.HomeTeamId = dto.Teams.Home.Id;
        match.AwayTeamId = dto.Teams.Away.Id;
        match.HomeTeamLogoUrl = dto.Teams.Home.Logo;
        match.AwayTeamLogoUrl = dto.Teams.Away.Logo;
        match.KickoffTime = dto.Fixture.Date.UtcDateTime;

        var newStatus = DeriveStatus(dto.Fixture.Status.Short, match.Status, externalId);
        var isHalfTime = dto.Fixture.Status.Short == "HT";

        // Captured before overwriting below - MatchAlertService.NotifyAsync needs both sides of
        // each transition (e.g. "was Upcoming, is now InProgress"), not just the new value.
        var previousStatus = match.Status;
        var previousHomeScore = match.HomeScore;
        var previousAwayScore = match.AwayScore;
        var previousIsHalfTime = match.IsHalfTime;

        var changed = match.HomeScore != dto.Goals?.Home || match.AwayScore != dto.Goals?.Away
            || match.Status != newStatus || match.Minute != dto.Fixture.Status.Elapsed
            || match.AddedTimeMinutes != dto.Fixture.Status.Extra || match.IsHalfTime != isHalfTime
            || match.HomePenalties != dto.Score?.Penalty?.Home || match.AwayPenalties != dto.Score?.Penalty?.Away;

        match.HomeScore = dto.Goals?.Home;
        match.AwayScore = dto.Goals?.Away;
        match.Status = newStatus;
        match.Minute = dto.Fixture.Status.Elapsed;
        match.AddedTimeMinutes = dto.Fixture.Status.Extra;
        match.IsHalfTime = isHalfTime;
        match.HomePenalties = dto.Score?.Penalty?.Home;
        match.AwayPenalties = dto.Score?.Penalty?.Away;
        match.WentToExtraTime = dto.Score?.Extratime?.Home is not null;

        if (changed)
        {
            await hub.Clients.All.SendAsync(
                "MatchUpdated",
                new MatchLiveUpdate(
                    match.Id, match.HomeScore, match.AwayScore, newStatus.ToString(), match.Minute,
                    match.AddedTimeMinutes, isHalfTime, match.HomePenalties, match.AwayPenalties, match.WentToExtraTime),
                ct);
        }

        if (!isNewMatch)
        {
            await FireLiveAlertsAsync(match, previousStatus, previousHomeScore, previousAwayScore, previousIsHalfTime, newStatus, ct);
        }

        return true;
    }

    // Every FK onto Matches is ON DELETE CASCADE, BetLegs included - removing a match someone has
    // bet on would silently erase that bet's legs, so those rows are left alone for a human to sort
    // out. Same for a BetBuilderBoost, whose cascade would wipe that day's featured boost.
    private async Task<bool> RemoveIneligibleMatchAsync(Match match, string? round, CancellationToken ct)
    {
        var hasBets = await db.BetLegs.AnyAsync(l => l.MatchId == match.Id, ct);
        var hasBoost = await db.BetBuilderBoosts.AnyAsync(b => b.MatchId == match.Id, ct);
        if (hasBets || hasBoost)
        {
            logger.LogWarning(
                "Match {MatchId} ({HomeTeam} v {AwayTeam}) is now in ineligible FA Cup round {Round} but has bets or a boost on it - not removing",
                match.Id, match.HomeTeam, match.AwayTeam, round);
            return false;
        }

        db.Matches.Remove(match);
        logger.LogInformation(
            "Removed match {MatchId} ({HomeTeam} v {AwayTeam}) - FA Cup round {Round} is not tracked",
            match.Id, match.HomeTeam, match.AwayTeam, round);
        return true;
    }

    // Kickoff/half-time/full-time/goal alerts - everything MatchAlertService needs to decide who
    // gets pushed already lives on `match` (post-overwrite, i.e. the new state) plus the "previous"
    // values captured just before UpsertMatchAsync overwrote them. Deliberately not gated on
    // MatchStatus == InProgress the way the red-card diff in ApiFootballSettlementSupportService is -
    // full-time is exactly the transition INTO Finished, so it must still fire that one tick.
    private async Task FireLiveAlertsAsync(
        Match match, MatchStatus previousStatus, int? previousHomeScore, int? previousAwayScore,
        bool previousIsHalfTime, MatchStatus newStatus, CancellationToken ct)
    {
        if (previousStatus == MatchStatus.Upcoming && newStatus == MatchStatus.InProgress)
        {
            await matchAlerts.NotifyAsync(
                match, MatchAlertType.Kickoff, "Kick-off!", $"{match.HomeTeam} v {match.AwayTeam} is underway",
                sequence: 0, ct: ct);
        }

        if (!previousIsHalfTime && match.IsHalfTime)
        {
            await matchAlerts.NotifyAsync(
                match, MatchAlertType.HalfTime, "Half-time",
                $"{match.HomeTeam} {match.HomeScore ?? 0}-{match.AwayScore ?? 0} {match.AwayTeam}",
                sequence: 0, ct: ct);
        }

        if (previousStatus != MatchStatus.Finished && newStatus == MatchStatus.Finished)
        {
            var penaltiesSuffix = match.HomePenalties is { } homePens && match.AwayPenalties is { } awayPens
                ? $" ({homePens}-{awayPens} pens)"
                : "";
            await matchAlerts.NotifyAsync(
                match, MatchAlertType.FullTime, "Full-time",
                $"{match.HomeTeam} {match.HomeScore ?? 0}-{match.AwayScore ?? 0} {match.AwayTeam}{penaltiesSuffix}",
                sequence: 0, ct: ct);
        }

        // Sequence needs to be unique per goal, including the rare case of both sides scoring in
        // the same tick (a missed poll in between) - two separate checks below, not one combined
        // "did the score change" check, so both still send. A shared "total goals so far" counter
        // would collide whenever both sides' scores increase by the same amount in one tick (e.g.
        // 1-1 -> 2-2 between polls: newHome+oldAway == oldHome+newAway), so home and away goals get
        // disjoint numeric ranges instead - the new home score itself for a home goal, offset by
        // 1000 for an away goal. No real match reaches 1000 goals, so these ranges never overlap.
        if ((match.HomeScore ?? 0) > (previousHomeScore ?? 0))
        {
            await matchAlerts.NotifyAsync(
                match, MatchAlertType.Goal, $"GOAL ({match.HomeTeam})",
                $"{match.HomeTeam} {match.HomeScore ?? 0}-{match.AwayScore ?? 0} {match.AwayTeam}",
                sequence: match.HomeScore ?? 0, ct: ct);
        }

        if ((match.AwayScore ?? 0) > (previousAwayScore ?? 0))
        {
            await matchAlerts.NotifyAsync(
                match, MatchAlertType.Goal, $"GOAL ({match.AwayTeam})",
                $"{match.HomeTeam} {match.HomeScore ?? 0}-{match.AwayScore ?? 0} {match.AwayTeam}",
                sequence: 1000 + (match.AwayScore ?? 0), ct: ct);
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
            case "NS" or "TBD":
                return MatchStatus.Upcoming;
            case "PST":
                return MatchStatus.Postponed;
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

    // FA Cup's qualifying rounds and 1st/2nd Round Proper are entirely non-league/lower-league
    // clubs API-Football's own live-status and the-odds-api's bookmaker coverage both handle poorly
    // (confirmed repeatedly during the cutover: matches stuck at "NS" long past their real kickoff,
    // zero odds coverage) - requested to only show FA Cup from the 3rd Round Proper onwards, which
    // is also when Championship/Premier League clubs actually enter the competition. Every other
    // tracked league is unaffected (round only matters for a knockout competition's early rounds).
    // Matched by exclusion rather than an exact-string allow-list since API-Football hadn't
    // published this season's later round names yet when this was written (only "1st Round
    // Qualifying" existed) - "contains Qualifying" or "starts with 1st/2nd Round" (its own Replays
    // included) covers every round before 3rd Round Proper regardless of exact suffix formatting.
    // The label alone isn't enough, though - API-Football has mislabelled a 2nd Round Qualifying
    // tie as "Quarter-finals" (Exmouth v Thame United, 2026-09-23), which no label rule can catch.
    // 3rd Round Proper is always the first weekend of January and 2nd Round Proper early December,
    // so any FA Cup fixture dated July-December is pre-3rd-Round whatever it's called.
    private static bool IsEligibleFaCupRound(FixtureDto fixture)
    {
        var league = fixture.League;
        if (league.Id != ApiFootballLeagueMap.FaCup)
        {
            return true;
        }

        if (fixture.Fixture.Date.UtcDateTime.Month >= 7)
        {
            return false;
        }

        var round = league.Round;
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
