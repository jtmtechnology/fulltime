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

            try
            {
                await service.ResolveFirstGoalScorersAsync(stoppingToken);
                await service.ResolvePlayerStatsAsync(stoppingToken);
                logger.LogInformation("API-Football settlement support tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football settlement support tick failed");
            }

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
