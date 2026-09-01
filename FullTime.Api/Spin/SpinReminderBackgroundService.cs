using FullTime.Api.Betting;
using Microsoft.Extensions.Options;

namespace FullTime.Api.Spin;

public class SpinReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<BettingOptions> options,
    ILogger<SpinReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var reminderService = scope.ServiceProvider.GetRequiredService<SpinReminderService>();

            try
            {
                await reminderService.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Spin reminder tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.SpinReminderCheckIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
