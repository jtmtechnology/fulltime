using Microsoft.Extensions.Options;

namespace FullTime.Api.Betting;

public class SettlementSweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<BettingOptions> options,
    ILogger<SettlementSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var settlementService = scope.ServiceProvider.GetRequiredService<SettlementService>();

            try
            {
                await settlementService.SweepAsync(stoppingToken);
                logger.LogInformation("Settlement sweep tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A tick failing must not take the whole host down — just log and retry next tick.
                logger.LogError(ex, "Settlement sweep tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.SweepIntervalSeconds)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
