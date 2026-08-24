using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder;

// Runs independently of BetBuilderSyncBackgroundService's odds-sync loop - resolving a match's first
// goalscorer is what unblocks a FirstTeamToScore bet's settlement (see
// SettlementService.ResolvePicksAsync's guard), so it needs to run on a settlement-latency cadence,
// not the far slower price-freshness one.
public class GoalScorerResolutionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<HighlightlyOptions> options,
    ILogger<GoalScorerResolutionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<BetBuilderSyncService>();

            try
            {
                await syncService.ResolveFirstGoalScorersAsync(stoppingToken);
                logger.LogInformation("Goal scorer resolution tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background goal scorer resolution tick failed");
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
