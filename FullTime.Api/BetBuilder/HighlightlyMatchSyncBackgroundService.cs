namespace FullTime.Api.BetBuilder;

public class HighlightlyMatchSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
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
                await syncService.RefreshLiveAsync(stoppingToken);
                logger.LogInformation("Live match sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing (e.g. the provider throttling us) must not take the whole host down.
                logger.LogError(ex, "Background live match sync tick failed");
            }

            var delay = await syncService.NextPollDelayAsync(stoppingToken);

            try
            {
                await Task.Delay(delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
