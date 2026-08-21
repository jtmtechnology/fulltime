using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

public class BetBuilderSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<BetBuilderSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On a cold start, this can otherwise race HighlightlyMatchSyncBackgroundService's own
        // first tick: if this runs first, every match still has its pre-restart LeagueId/ExternalId
        // and nothing matches HighlightlyLeagueMap yet, so this tick prices nothing and then waits
        // its full SyncIntervalMinutes (up to an hour) before trying again. A short settle delay
        // gives the match sync's first tick (observed to take well under a minute) time to land.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<BetBuilderSyncService>();

            try
            {
                await syncService.RefreshAsync(stoppingToken);
                logger.LogInformation("Bet-builder sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing (e.g. the odds provider throttling us) must not take the whole host down.
                logger.LogError(ex, "Background bet-builder sync tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.SyncIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
