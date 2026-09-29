using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

public class ApiFootballSettlementSupportBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiFootballOptions> options,
    ILogger<ApiFootballSettlementSupportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ApiFootballSettlementSupportService>();

            // Separate so a failure in one (e.g. the candidate query) can't also skip the other.
            try
            {
                await service.ResolveMatchEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football match events resolution failed");
            }

            try
            {
                await service.ResolvePlayerStatsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football player stats resolution failed");
            }

            logger.LogInformation("API-Football settlement support tick complete");

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.GoalScorerResolutionIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
