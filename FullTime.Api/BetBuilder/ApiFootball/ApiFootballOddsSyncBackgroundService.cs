using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Proactive counterpart to the-odds-api's on-demand-only OddsApiMarketService - the goal here is
// "basic odds ready for every tracked Upcoming match" (per the owner's request to have odds
// available wherever markets exist), not just whichever match a user happens to view, mirroring
// Highlightly's own always-on BetBuilderSyncService. Iterates every tracked Upcoming match on each
// tick; ApiFootballOddsService.EnsureBetBuilderMarketsFreshAsync's own TTL gate makes most of those
// per-match calls same-tick no-ops, so a short OddsSyncIntervalMinutes doesn't itself drive up call
// volume - the TTL tiers do.
public class ApiFootballOddsSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ApiFootballOptions> options,
    ILogger<ApiFootballOddsSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
            var oddsService = scope.ServiceProvider.GetRequiredService<ApiFootballOddsService>();

            try
            {
                var matches = await db.Matches
                    .Where(m => m.Status == Models.MatchStatus.Upcoming)
                    .ToListAsync(stoppingToken);

                foreach (var match in matches)
                {
                    await oddsService.EnsureBetBuilderMarketsFreshAsync(match, stoppingToken);
                }

                logger.LogInformation("API-Football odds sync tick complete ({Count} tracked match(es) checked)", matches.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Background API-Football odds sync tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.OddsSyncIntervalMinutes)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
