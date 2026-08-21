using FullTime.Api.BetBuilder.Dtos;
using FullTime.Api.Data;
using FullTime.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// The only caller of RefreshLiveAsync/RefreshFixturesAsync is HighlightlyMatchSyncBackgroundService's
// timer — same choke-point pattern as BetBuilderSyncService for the extra markets. This is now the
// primary sync: fixtures, live scores, and match status all come from Highlightly's global (no
// leagueId) matches-by-date endpoint, filtered to HighlightlyLeagueMap.TrackedLeagueIds — one
// paginated fetch per date instead of one call per tracked league.
//
// Split into two cadences to stay well within the 7,500 req/day RapidAPI quota: fixtures and prices
// barely change once set, so there's no need to re-poll them often, but a live match's score does —
// see the "Retire free-api-live-football-data" plan's rate-limit writeup for the numbers that led
// here (a global per-date fetch on a busy day can be 5-10+ pages; refetching several days of that
// every few minutes burned through the daily quota in under 10 hours in practice).
public class HighlightlyMatchSyncService(
    HighlightlyClient client,
    AppDbContext db,
    IOptions<HighlightlyOptions> options,
    ILogger<HighlightlyMatchSyncService> logger)
{
    // Today's date plus any not-yet-Finished match's own kickoff date (a match that kicked off but
    // never reached Finished — e.g. the day rolled over before the provider reported a final score —
    // would otherwise never get re-synced and never settle). Deliberately NOT the full future
    // window: only matches that could plausibly be live right now need this frequent a refresh.
    public async Task RefreshLiveAsync(CancellationToken ct = default)
    {
        var datesToSync = new HashSet<DateOnly> { DateOnly.FromDateTime(DateTime.UtcNow) };

        var unfinishedKickoffs = await db.Matches
            .Where(m => m.Status != MatchStatus.Finished)
            .Select(m => m.KickoffTime)
            .ToListAsync(ct);

        foreach (var kickoff in unfinishedKickoffs)
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

    private async Task<int> FetchAndUpsertDateAsync(DateOnly date, CancellationToken ct)
    {
        var offset = 0;
        var count = 0;

        while (true)
        {
            MatchesResponse? page;
            try
            {
                page = await client.GetAllMatchesAsync(date, offset, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch matches for {Date} at offset {Offset}", date, offset);
                break;
            }

            if (page is null || page.Data.Count == 0)
            {
                break;
            }

            foreach (var dto in page.Data.Where(m => HighlightlyLeagueMap.TrackedLeagueIds.Contains(m.League.Id)))
            {
                await UpsertMatchAsync(dto, ct);
                count++;
            }

            offset += page.Data.Count;
            if (page.Pagination is null || offset >= page.Pagination.TotalCount)
            {
                break;
            }
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return count;
    }

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
        match.HomeTeam = dto.HomeTeam.Name;
        match.AwayTeam = dto.AwayTeam.Name;
        match.HomeTeamId = dto.HomeTeam.Id;
        match.AwayTeamId = dto.AwayTeam.Id;
        match.HomeTeamLogoUrl = dto.HomeTeam.Logo;
        match.AwayTeamLogoUrl = dto.AwayTeam.Logo;
        match.KickoffTime = dto.Date;

        var (homeScore, awayScore) = ParseScore(dto.State.Score?.Current);
        match.HomeScore = homeScore;
        match.AwayScore = awayScore;
        match.Status = DeriveStatus(dto.State.Description);
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
    // time", "Finished after penalties", "Postponed". No live match was observed while building
    // this, so anything else (including any yet-unseen live-state string) defaults to InProgress
    // rather than requiring an exact match — verify against a real live match once deployed.
    // Postponed maps to Upcoming (no dedicated MatchStatus for it); its score naturally parses to
    // null since the provider reports none.
    private static MatchStatus DeriveStatus(string description)
    {
        if (description is "Not started" or "Postponed")
        {
            return MatchStatus.Upcoming;
        }

        return description.StartsWith("Finished", StringComparison.Ordinal)
            ? MatchStatus.Finished
            : MatchStatus.InProgress;
    }
}
