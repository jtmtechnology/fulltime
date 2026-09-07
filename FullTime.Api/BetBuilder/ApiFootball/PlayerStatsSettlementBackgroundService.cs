using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Always-on, independent of Providers:LiveScoreSource - unlike ApiFootballSettlementSupportBackgroundService
// (which also resolves FirstTeamToScore and only runs in the dormant LiveScoreSource=ApiFootball
// cutover, since Highlightly's own GoalScorerResolutionBackgroundService already handles that
// market correctly), player-shots settlement (ResolvePlayerStatsAsync) has no Highlightly
// equivalent at all - Highlightly has no per-player stats of any kind, confirmed real 2026-09-07 -
// so this must run regardless of which provider is live, or bets on PlayerShotsOnTarget/PlayerShots
// (see PlayerPropsService, MarketType.cs) would never settle. Re-added 2026-09-07 after briefly
// being retired the same day - MatchPlayerStat now only needs to carry shots data (goals/assists/
// cards moved to Highlightly's MatchEvent), but the resolution call itself is unchanged.
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
                logger.LogInformation("Player-shots settlement tick complete");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background player-shots settlement tick failed");
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
