using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

public class BetBuilderSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<BetBuilderSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<BetBuilderSyncService>();

            var foundMatches = false;
            try
            {
                foundMatches = await syncService.RefreshAsync(stoppingToken);
                logger.LogInformation("Bet-builder sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing (e.g. the odds provider throttling us) must not take the whole host down.
                logger.LogError(ex, "Background bet-builder sync tick failed");
            }

            // On a cold start this can race HighlightlyMatchSyncBackgroundService's own first tick —
            // if this runs first, nothing matches HighlightlyLeagueMap yet and RefreshAsync finds
            // nothing to price. With SyncIntervalMinutes now a full day, waiting for the normal
            // interval before retrying would leave odds blank for the rest of the day, so retry soon
            // instead whenever a tick found nothing (a real "no upcoming matches" case retries this
            // often too, but that's harmless — it's just an empty query).
            var delay = foundMatches
                ? TimeSpan.FromMinutes(Math.Max(1, options.Value.SyncIntervalMinutes))
                : TimeSpan.FromMinutes(2);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
