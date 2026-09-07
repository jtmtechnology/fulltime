using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Always-on, independent of Providers:LiveScoreSource - unlike ApiFootballSettlementSupportBackgroundService
// (which also resolves FirstTeamToScore and only runs in the dormant LiveScoreSource=ApiFootball
// cutover, since Highlightly's own GoalScorerResolutionBackgroundService already handles that
// market correctly), player-prop settlement (ResolvePlayerStatsAsync) has no Highlightly equivalent
// at all - Highlightly doesn't have player-level stats - so this must run regardless of which
// provider is live, or bets on PlayerGoalscorerAnytime/PlayerCard/PlayerShotsOnTarget/PlayerAssists
// (see PlayerPropsService) would never settle. Confirmed real 2026-09-07: this was the actual gap
// before this file existed.
public class PlayerStatsSettlementBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiFootballOptions> options,
    ILogger<PlayerStatsSettlementBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ApiFootballSettlementSupportService>();

            try
            {
                await service.ResolvePlayerStatsAsync(stoppingToken);
                logger.LogInformation("Player-prop settlement tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background player-prop settlement tick failed");
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
