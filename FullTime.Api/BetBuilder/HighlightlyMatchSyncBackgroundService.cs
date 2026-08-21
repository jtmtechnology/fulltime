using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

public class HighlightlyMatchSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<HighlightlyMatchSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<HighlightlyMatchSyncService>();

            try
            {
                await syncService.RefreshMatchesAsync(stoppingToken);
                logger.LogInformation("Match sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing (e.g. the provider throttling us) must not take the whole host down.
                logger.LogError(ex, "Background match sync tick failed");
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
