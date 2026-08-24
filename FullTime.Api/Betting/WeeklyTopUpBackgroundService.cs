using Microsoft.Extensions.Options;

namespace FullTime.Api.Betting;

public class WeeklyTopUpBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<BettingOptions> options,
    ILogger<WeeklyTopUpBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var topUpService = scope.ServiceProvider.GetRequiredService<WeeklyTopUpService>();

            try
            {
                await topUpService.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Weekly top-up tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.WeeklyTopUpCheckIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
