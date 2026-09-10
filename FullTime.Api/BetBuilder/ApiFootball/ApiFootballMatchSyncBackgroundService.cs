namespace FullTime.Api.BetBuilder.ApiFootball;

public class ApiFootballMatchSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ApiFootballMatchSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ApiFootballMatchSyncService>();
            var settlementSupportService = scope.ServiceProvider.GetRequiredService<ApiFootballSettlementSupportService>();

            try
            {
                await syncService.RefreshLiveAsync(stoppingToken);
                logger.LogInformation("API-Football live match sync tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football live match sync tick failed");
            }

            try
            {
                await settlementSupportService.RefreshLiveMatchEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football live match events refresh failed");
            }

            try
            {
                await syncService.CheckStaleInProgressMatchesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background stale-InProgress check failed");
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
