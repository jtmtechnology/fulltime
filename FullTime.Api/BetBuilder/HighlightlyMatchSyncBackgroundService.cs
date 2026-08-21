using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

public class HighlightlyMatchSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<HighlightlyMatchSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs on every tick regardless of cadence — fixture discovery (today+1..N) is comparatively
        // rare and gets its own timer here rather than a separate hosted service, since it shares
        // the same scoped HighlightlyMatchSyncService and DB context pattern.
        var lastFixtureDiscoveryUtc = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<HighlightlyMatchSyncService>();

            try
            {
                await syncService.RefreshLiveAsync(stoppingToken);
                logger.LogInformation("Live match sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing (e.g. the provider throttling us) must not take the whole host down.
                logger.LogError(ex, "Background live match sync tick failed");
            }

            var dueForFixtureDiscovery = DateTime.UtcNow - lastFixtureDiscoveryUtc
                >= TimeSpan.FromMinutes(options.Value.FixtureDiscoveryIntervalMinutes);
            if (dueForFixtureDiscovery)
            {
                try
                {
                    await syncService.RefreshFixturesAsync(stoppingToken);
                    lastFixtureDiscoveryUtc = DateTime.UtcNow;
                    logger.LogInformation("Fixture discovery tick complete");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Background fixture discovery tick failed");
                }
            }

            var hasLiveMatch = await syncService.HasLiveMatchAsync(stoppingToken);
            var seconds = hasLiveMatch ? options.Value.LiveRefreshIntervalSeconds : options.Value.IdleRefreshIntervalSeconds;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
