using FullTime.Api.Data;
using FullTime.Api.OddsApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.OddsApi;

// The only caller of RefreshUpcomingMatchesAsync is MatchSyncBackgroundService's timer.
// All other requests (the controller, any date filter) read whatever's already in the DB —
// this is the single choke point for calls to the external odds/scores provider.
public class MatchSyncService(
    FootballDataClient client,
    AppDbContext db,
    IOptions<OddsApiOptions> options,
    ILogger<MatchSyncService> logger)
{
    public async Task RefreshUpcomingMatchesAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var datesToSync = Enumerable.Range(0, opts.DaysAhead)
            .Select(i => today.AddDays(i))
            .ToHashSet();

        // A match that kicked off but never reached Finished (e.g. the day rolled over before the
        // provider reported a final score) would otherwise fall out of the DaysAhead window forever
        // and never settle — keep re-syncing its date until it actually finishes.
        var unfinishedKickoffs = await db.Matches
            .Where(m => m.Status != Models.MatchStatus.Finished)
            .Select(m => m.KickoffTime)
            .ToListAsync(ct);

        foreach (var kickoff in unfinishedKickoffs)
        {
            datesToSync.Add(DateOnly.FromDateTime(kickoff));
        }

        foreach (var date in datesToSync)
        {
            await FetchAndUpsertDateAsync(date, opts, ct);
        }
    }

    public Task<bool> HasLiveMatchAsync(CancellationToken ct = default) =>
        db.Matches.AnyAsync(m => m.Status == Models.MatchStatus.InProgress, ct);

    private async Task FetchAndUpsertDateAsync(DateOnly date, OddsApiOptions opts, CancellationToken ct)
    {
        List<MatchDto> matches;
        try
        {
            matches = await client.GetMatchesByDateAsync(date, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch matches for {Date}", date);
            return;
        }

        foreach (var dto in matches.Where(m => opts.LeagueIds.Contains(m.LeagueId)))
        {
            await UpsertMatchAsync(dto, opts, ct);
        }
    }

    private async Task UpsertMatchAsync(MatchDto dto, OddsApiOptions opts, CancellationToken ct)
    {
        var externalId = dto.Id.ToString();
        var match = await db.Matches.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct);
        if (match is null)
        {
            match = new Models.Match
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                HomeTeam = dto.Home.Name,
                AwayTeam = dto.Away.Name,
                KickoffTime = dto.Status.UtcTime,
                Status = Models.MatchStatus.Upcoming
            };
            db.Matches.Add(match);
        }

        match.LeagueId = dto.LeagueId;
        match.HomeTeamId = dto.Home.Id;
        match.AwayTeamId = dto.Away.Id;
        match.HomeScore = dto.Status.Started ? dto.Home.Score : null;
        match.AwayScore = dto.Status.Started ? dto.Away.Score : null;
        match.Status = dto.Status.Finished
            ? Models.MatchStatus.Finished
            : dto.Status.Started
                ? Models.MatchStatus.InProgress
                : Models.MatchStatus.Upcoming;

        // Pre-match odds stop being meaningful once a match kicks off, so only sync them beforehand.
        if (!dto.Status.Started)
        {
            await SnapshotOddsIfChangedAsync(match, dto.Id, opts.CountryCode, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SnapshotOddsIfChangedAsync(Models.Match match, long eventId, string countryCode, CancellationToken ct)
    {
        EventOddsResponse? oddsResponse;
        try
        {
            oddsResponse = await client.GetEventOddsAsync(eventId, countryCode, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch odds for event {EventId}", eventId);
            return;
        }

        var parsed = oddsResponse is null ? null : MatchOddsParser.Parse(oddsResponse);
        if (parsed is null)
        {
            return;
        }

        var latest = await db.OddsSnapshots
            .Where(o => o.MatchId == match.Id)
            .OrderByDescending(o => o.FetchedAt)
            .FirstOrDefaultAsync(ct);

        var changed = latest is null
            || latest.HomeOdds != parsed.HomeOdds
            || latest.DrawOdds != parsed.DrawOdds
            || latest.AwayOdds != parsed.AwayOdds
            || (latest.BookmakerLogoUrl is null && parsed.BookmakerLogoUrl is not null);

        if (!changed)
        {
            return;
        }

        db.OddsSnapshots.Add(new Models.OddsSnapshot
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            HomeOdds = parsed.HomeOdds,
            DrawOdds = parsed.DrawOdds,
            AwayOdds = parsed.AwayOdds,
            Bookmaker = parsed.Bookmaker,
            BookmakerLogoUrl = parsed.BookmakerLogoUrl,
            FetchedAt = DateTime.UtcNow
        });
    }
}
