using FullTime.Api.BetBuilder.Dtos;

namespace FullTime.Api.BetBuilder.ApiFootball;

// Powers the Match Summary sheet's Player Stats tab. Backed by API-Football's /fixtures/players
// (see ApiFootballClient.GetFixturePlayerStatsAsync) - the same call
// ApiFootballSettlementSupportService already uses for goalscorer/card/shots settlement, but only
// ever post-Finished on a background timer. Same short-TTL on-demand cache shape as
// ApiFootballMatchStatsService, for the same reason: a match still being played should show
// close-to-live per-player numbers each time someone opens the tab, without a background poll.
public class ApiFootballPlayerStatsService(ApiFootballClient client, ILogger<ApiFootballPlayerStatsService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(45);
    private static readonly SemaphoreSlim CacheLock = new(1, 1);
    private static readonly Dictionary<long, (List<FixturePlayersResponseTeam> Teams, DateTime FetchedAt)> Cache = new();

    public async Task<List<FixturePlayersResponseTeam>> GetPlayerStatsAsync(long fixtureId, CancellationToken ct = default)
    {
        await CacheLock.WaitAsync(ct);
        try
        {
            if (Cache.TryGetValue(fixtureId, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return cached.Teams;
            }
        }
        finally
        {
            CacheLock.Release();
        }

        List<FixturePlayersResponseTeam> teams;
        try
        {
            teams = await client.GetFixturePlayerStatsAsync(fixtureId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to fetch player statistics for fixture {FixtureId}", fixtureId);
            teams = [];
        }

        await CacheLock.WaitAsync(ct);
        try
        {
            Cache[fixtureId] = (teams, DateTime.UtcNow);
        }
        finally
        {
            CacheLock.Release();
        }

        return teams;
    }
}
