using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// Runs independently of HighlightlyMatchSyncBackgroundService's live-score loop - discovery used to
// share that loop and block it for however long a full today..today+N-1 sweep took (confirmed ~50s),
// delaying live-score updates any time discovery happened to be due during a live match window.
public class HighlightlyFixtureDiscoveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<HighlightlyFixtureDiscoveryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<HighlightlyMatchSyncService>();

            try
            {
                await syncService.RefreshFixturesAsync(stoppingToken);
                logger.LogInformation("Fixture discovery tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background fixture discovery tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(options.Value.FixtureDiscoveryIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
