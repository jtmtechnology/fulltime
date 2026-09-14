using FullTime.Api.Data;
using FullTime.Api.Models;
using FullTime.Api.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FullTime.Api.BetBuilder.ApiFootball;

// The one alert type with no existing sync pass to hook into - lineups aren't stored anywhere,
// only fetched on-demand (ApiFootballLineupsService) when someone opens Match Details, unlike
// score/status (already synced every live tick) or events (already synced on their own timer). This
// is a deliberate, narrow exception to the app's usual no-background-polling preference: it only
// calls out for matches that (a) kick off within the next ~90 minutes, real lineups typically land
// ~60 minutes out, and (b) already have at least one real subscriber with LineupsOut turned on -
// not blanket polling of every tracked fixture, and a quiet day with no subscribers costs nothing
// beyond the one DB query per tick.
public class MatchAlertLineupsCheckBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<MatchAlertLineupsCheckBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LookaheadWindow = TimeSpan.FromMinutes(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lineupsService = scope.ServiceProvider.GetRequiredService<ApiFootballLineupsService>();
            var matchAlerts = scope.ServiceProvider.GetRequiredService<MatchAlertService>();

            try
            {
                await CheckAsync(db, lineupsService, matchAlerts, stoppingToken);
                logger.LogInformation("Lineups-check tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background lineups-check tick failed");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAsync(
        AppDbContext db, ApiFootballLineupsService lineupsService, MatchAlertService matchAlerts, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var windowEnd = now.Add(LookaheadWindow);

        // The "does anyone actually want this" check is repeated here (not just left to
        // MatchAlertService.NotifyAsync's own scope+preference resolution) specifically so a match
        // nobody's asked about never even reaches GetLineupsAsync - the whole point of keeping this
        // narrow rather than checking every Upcoming fixture in the window.
        var candidates = await db.Matches
            .Where(m => m.Status == MatchStatus.Upcoming && m.KickoffTime > now && m.KickoffTime <= windowEnd)
            .Where(m => db.Users.Any(u =>
                (db.FavouriteTeams.Any(f => f.UserId == u.Id && (f.TeamId == m.HomeTeamId || f.TeamId == m.AwayTeamId))
                    || db.FavouriteLeagues.Any(f => f.UserId == u.Id && f.LeagueId == m.LeagueId)
                    || db.MatchAlertSubscriptions.Any(s => s.UserId == u.Id && s.MatchId == m.Id))
                && db.UserAlertPreferences.Any(p => p.UserId == u.Id && p.LineupsOut)))
            .Where(m => !db.SentMatchAlerts.Any(s => s.MatchId == m.Id && s.AlertType == MatchAlertType.LineupsOut))
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return;
        }

        foreach (var match in candidates)
        {
            if (!long.TryParse(match.ExternalId, out var fixtureId))
            {
                continue;
            }

            List<Dtos.FixtureLineupTeam> lineups;
            try
            {
                lineups = await lineupsService.GetLineupsAsync(fixtureId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to check lineups for match {MatchId}", match.Id);
                continue;
            }

            if (!lineups.Any(t => t.StartXI.Count > 0))
            {
                continue;
            }

            await matchAlerts.NotifyAsync(
                match, MatchAlertType.LineupsOut, "Lineups are out",
                $"{match.HomeTeam} v {match.AwayTeam} team news is in", sequence: 0, ct: ct);
        }
    }
}
