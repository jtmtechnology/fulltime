using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Powers the Match Summary sheet's Stats tab. Backed by API-Football's /fixtures/statistics (see
// ApiFootballClient.GetFixtureStatisticsAsync) - the same call ApiFootballSettlementSupportService
// already uses for corners/cards, but that one only ever runs post-Finished on a background timer.
// This is a short-TTL on-demand cache instead (same shape as ApiFootballTeamFormService) so a match
// still being played gets close-to-live numbers each time someone opens the sheet, without a
// background poll - per the standing no-background-polling preference, the cost of a fresh fetch is
// only ever paid by an actual viewer, not a timer.
public class ApiFootballMatchStatsService(ApiFootballClient client, ILogger<ApiFootballMatchStatsService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<long, (List<FixtureStatisticsTeam> Stats, DateTime FetchedAt)> Cache = new();

    public async Task<List<FixtureStatisticsTeam>> GetStatisticsAsync(long fixtureId, CancellationToken ct = default)
    {
        await CacheLock.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(fixtureId, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached.Stats;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        List<FixtureStatisticsTeam> stats;
        try
        {
            stats = await client.GetFixtureStatisticsAsync(fixtureId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A match too early to have any stats yet and a throttled/failed call both just mean "no
            // rows to show" - cache the miss too (same TTL) so a bad request isn't retried on every
            // single sheet open until the cache would naturally expire.
            logger.LogWarning(ex, "Failed to fetch match statistics for fixture {FixtureId}", fixtureId);
            stats = [];
        }

        await CacheLock.WaitAsync(ct);
        try
        {
            Cache[fixtureId] = (stats, DateTime.UtcNow);
        }
        finally
        {
            CacheLock.Release();
        }

        return stats;
    }
}
