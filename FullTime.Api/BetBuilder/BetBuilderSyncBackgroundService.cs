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
