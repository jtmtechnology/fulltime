using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Powers the Match Summary sheet's Lineups tab. Backed by API-Football's /fixtures/lineups (see
// ApiFootballClient.GetFixtureLineupsAsync) - same short-TTL on-demand cache shape as
// ApiFootballMatchStatsService/ApiFootballPlayerStatsService, even though a lineup itself is
// essentially frozen once announced (unlike stats/player-stats, which move all match long) - keeping
// the same pattern here is simpler than reasoning about a different TTL for one endpoint, and the
// cost of a slightly-too-eager refetch is negligible against a 75,000/day budget.
public class ApiFootballLineupsService(ApiFootballClient client, ILogger<ApiFootballLineupsService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<long, (List<FixtureLineupTeam> Lineups, DateTime FetchedAt)> Cache = new();

    public async Task<List<FixtureLineupTeam>> GetLineupsAsync(long fixtureId, CancellationToken ct = default)
    {
        await CacheLock.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(fixtureId, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached.Lineups;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        List<FixtureLineupTeam> lineups;
        try
        {
            lineups = await client.GetFixtureLineupsAsync(fixtureId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not yet announced (too far before kickoff) and a throttled/failed call both just mean
            // "no rows to show" - cache the miss too (same TTL) so a bad request isn't retried on
            // every single sheet open until the cache would naturally expire.
            logger.LogWarning(ex, "Failed to fetch lineups for fixture {FixtureId}", fixtureId);
            lineups = [];
        }

        await CacheLock.WaitAsync(ct);
        try
        {
            Cache[fixtureId] = (lineups, DateTime.UtcNow);
        }
        finally
        {
            CacheLock.Release();
        }

        return lineups;
    }
}
