using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

public class ApiFootballFixtureDiscoveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiFootballOptions> options,
    ILogger<ApiFootballFixtureDiscoveryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ApiFootballMatchSyncService>();

            try
            {
                await syncService.RefreshFixturesAsync(stoppingToken);
                logger.LogInformation("API-Football fixture discovery tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football fixture discovery tick failed");
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
